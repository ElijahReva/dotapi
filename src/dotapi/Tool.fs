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
    
    let ensureDirectory path = 
        if not (Directory.Exists path) then Directory.CreateDirectory(path) else (DirectoryInfo path) 
        |> (fun x -> x.FullName) 
        
    let inline pathCombine child parrent = Path.Combine(parrent, child)
    
    let collectWith splitter (str: string seq) =        
        ((StringBuilder(), str) 
        ||> Seq.fold (fun sb (str : string) -> sb.AppendFormat("{0}{1}", str, splitter)))
        |> string


[<CliPrefix(CliPrefix.Dash)>]
type DescribeArgs =
    | [<MainCommand>]                           Input    of files : string list
    | [<AltCommandLine("-o")>]                  Output   of filePath: string
    | [<AltCommandLine("-d")>]                  Details
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Input _  -> "Binary files, project files or folders with project to analyze"
            | Output _ -> "Output file or directory"
            | Details  -> "Separate api description file per input item."
            
[<CliPrefix(CliPrefix.DoubleDash)>]
type Args =
    | [<CliPrefix(CliPrefix.None)>]             Describe of ParseResults<DescribeArgs>
    | [<Inherit; AltCommandLine("-q")>]         Quiet  
    | [<Inherit; AltCommandLine("-v")>]         Verbose    
    | [<Inherit; CliPrefix(CliPrefix.None)>]    Version    
        
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
    open System
    open PublicApiGenerator
    open System
    open System.Collections.Generic  
    
    let [<Literal>] private DefaultOutputFile = "api.txt"
     
    type private Result = 
        {
            Name: string
            Content: string
        }
    type private Writer = Result -> string
    
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
    
    let private generate : Assembly -> Result = 
        fun path -> 
            let name = Path.GetFileNameWithoutExtension(path.Location)
            { 
                Name = name
                Content = ApiGenerator.GeneratePublicApi path
            }            
    
    let private processUsing (writer: Writer) =
        List.choose filterBinaries
        >> List.map generate
        >> List.map writer    
        
    let private AsSingleFile path = 
        File.Delete(path)
        fun content -> 
            File.AppendAllText(path, Environment.NewLine)
            File.AppendAllText(path, content) 
            path 
            
    let private AsManyFiles path = 
        fun content ->            
            File.Delete(path)
            File.WriteAllText(path, content)
            path                         
    
    let private (|Folder|_|) (input: string) =
        let path = Path.GetFullPath(input)
        let chToStr ch = string ch    
        if path.EndsWith(chToStr Path.DirectorySeparatorChar) || 
           path.EndsWith(chToStr Path.AltDirectorySeparatorChar) then 
            Some input
        else
            None 
    
          
    let private WriteToFile isMany (path: string) : Writer =
        match isMany with 
        | true ->
            let fileName = Path.GetFileName(path)
            let directory = Path.GetDirectoryName(path)
            let factory name = Path.Combine(directory, (sprintf "%s.%s") name fileName) 
            fun (res: Result) -> 
                let path = factory res.Name
                if File.Exists path then File.Delete path
                let path = FileInfo(path).FullName
                File.WriteAllText(path, res.Content)
                path 
        | false ->
             if File.Exists path then File.Delete path
             let path = FileInfo(path).FullName
             fun (res: Result) ->
                 File.AppendAllText(path, "---")
                 File.AppendAllText(path, res.Name)
                 File.AppendAllText(path, "---")
                 File.AppendAllText(path, res.Content)
                 path + " --- " + res.Name
                       
    let private createWriter isMany output  : Writer =    
        match output with 
        | Folder folderPath ->
            folderPath 
            |> ensureDirectory
            |> pathCombine DefaultOutputFile          
            |> WriteToFile isMany            
        | filePath ->
            filePath             
            |> WriteToFile isMany 
                            
    
    let private Describe  (describe: ParseResults<DescribeArgs>) =
        let writer =
            (
                describe.Contains(<@ DescribeArgs.Details @>),
                describe.GetResult(<@ DescribeArgs.Output @>, DefaultOutputFile)
            ) ||> createWriter
            
        describe.GetResult(<@ DescribeArgs.Input @>, [Environment.CurrentDirectory])
        |> processUsing writer
        |> collectWith Environment.NewLine    
               
    let private Main (describe: ParseResults<Args>) =
        "Not implemented"
    
    let private Version =
        let attribute = Assembly.GetAssembly(typeof<Args>).GetCustomAttribute<AssemblyInformationalVersionAttribute>()  
        match attribute with 
        | null -> "Develop"
        | x -> x.InformationalVersion
        
    let run (parseResults: ParseResults<Args>) =
        if parseResults.Contains <@ Args.Version @> then
            Version
        else 
            match parseResults.TryGetSubCommand() with 
            | Some (Describe args) -> Describe args 
            | _ -> Main parseResults
                                       
