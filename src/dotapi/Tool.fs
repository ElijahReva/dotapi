namespace dotapi


open PublicApiGenerator
open System.Reflection
open System.Runtime.Loader
open System.Reflection
open System.IO
open System
open System.Text
open Argu
open Serilog
open Serilog
open Serilog.Events

[<AutoOpen>]
module Helper =
    let collectWith splitter (str: string seq) =        
        ((StringBuilder(), str) 
        ||> Seq.fold (fun sb (str : string) -> sb.AppendFormat("{0}{1}", str, splitter)))
        |> string


[<CliPrefix(CliPrefix.Dash)>]
type DescribeArgs =
    | [<MainCommand>] Binaries of files : string list
    | [<AltCommandLine("-o")>] Output of filePath: string
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Binaries _ -> "Files to analyze"
            | Output _ ->  "Output file"
            
[<CliPrefix(CliPrefix.None)>]
type Args =
    | Describe of ParseResults<DescribeArgs>
    | [<Inherit; CliPrefix(CliPrefix.DoubleDash); AltCommandLine("-q")>] Quiet  
    | [<Inherit; CliPrefix(CliPrefix.DoubleDash); AltCommandLine("-v")>] Verbose    
    | [<Inherit>] Version    
        
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Describe _ -> "Create public api header file"
            | Version -> "Current version"
            | Quiet -> "Single result line in output"
            | Verbose -> "More logs in output"
            
module Api = 
    open System
    open System.Collections.Generic
    open System
    
    let [<Literal>] private DefaultOutputFile = "api.txt"
    
    let private fileExist path =
        if File.Exists path then
            path
        else failwithf "Invalid path %s" path
        
    let private filterBinaries = 
    
        let filterExitance path =
            let fi = new FileInfo(path)
            if fi.Exists then 
                fi.FullName |> Some
            else
                Log.Debug("File not found {Path}", path) 
                None
                
        let filterAssembly path = 
            let logError (ex: Exception) (event:string) (path:string) = 
               Log.Debug("{Event} {Path}", event, path) 
               Log.Verbose(ex, "{Event} {Path}", event, path) 
               
            try
                AssemblyLoadContext.Default.LoadFromAssemblyPath(path) |> Some
            with 
            | :? BadImageFormatException as biex ->
                logError biex "NotAnDotnetDll" path
                reraise()                
            | :? FileLoadException as flex -> 
                logError flex "AssemblyAlreadyLoaded" path
                reraise() 
                
        
        filterExitance 
        >> Option.bind filterAssembly
    
    let private generateDescription path = 
        path 
        |> AssemblyLoadContext.Default.LoadFromAssemblyPath
        |> ApiGenerator.GeneratePublicApi 
    
    let private processManyUsing saver =
        List.choose filterBinaries
        >> List.map ApiGenerator.GeneratePublicApi
        >> List.iter saver    
        
    let private AsSingleFile path = 
        File.Delete(path)
        fun file -> 
            File.AppendAllText(path, Environment.NewLine)
            File.AppendAllText(path, file)  
                
    let private processMany path = processManyUsing (AsSingleFile path)
        
    
    let private Describe  (describe: ParseResults<DescribeArgs>) =            
        let binaries = describe.GetResult (<@ DescribeArgs.Binaries @>)
        let outputPath = describe.GetResult(DescribeArgs.Output, DefaultOutputFile)
        binaries |> processMany outputPath                
        outputPath        
               
    let private Main (describe: ParseResults<Args>) =
        "Not implemented"
    
    let private getVersion () = 
        Assembly
            .GetAssembly(typeof<Args>)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            .InformationalVersion
        |> string
        
    let run (parseResults: ParseResults<Args>) =
        if parseResults.Contains <@ Args.Version @> then
            getVersion()
        else 
            match parseResults.TryGetSubCommand() with 
            | Some (Describe args) -> Describe args 
            | _ -> Main parseResults
                                       
