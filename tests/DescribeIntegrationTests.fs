namespace PublicApiGenerator.Tool.Tests

open FsUnit
open NHamcrest
open Xunit    
open System.Diagnostics
open System
open System.Linq
open System.Collections
open System.Collections.Generic
open System.IO
open Serilog.Core
open dotapi
open Microsoft.FSharp.Quotations
open Serilog.Events
open System.Diagnostics
open System.Runtime.CompilerServices

[<AutoOpen>]
module TestHelper =
    
    let private any<'R> : 'R = failwith "!"
    let private runProc printer filename args startDir = 
        let timer = Stopwatch.StartNew()
        let procStartInfo = 
            ProcessStartInfo(
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                FileName = filename,
                Arguments = args
            )
        match startDir with | Some d -> procStartInfo.WorkingDirectory <- d | _ -> ()

        let outputs = System.Collections.Generic.List<string>()
        let errors = System.Collections.Generic.List<string>()
        let outputHandler f (_sender:obj) (args:DataReceivedEventArgs) = f args.Data
        let p = new Process(StartInfo = procStartInfo)
        p.OutputDataReceived.AddHandler(DataReceivedEventHandler (outputHandler outputs.Add))
        p.ErrorDataReceived.AddHandler(DataReceivedEventHandler (outputHandler errors.Add))
        let started = 
            try
                p.Start()
            with | ex ->
                ex.Data.Add("filename", filename)
                reraise()
        if not started then
            failwithf "Failed to start process %s" filename
        sprintf "Started %s with pid %i" p.ProcessName p.Id |> printer
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()
        timer.Stop()
        sprintf "Finished %s after %A milliseconds" filename timer.ElapsedMilliseconds |> printer
        let cleanOut l = l |> Seq.filter (fun o -> String.IsNullOrEmpty o |> not)
        cleanOut outputs, cleanOut errors
    
    type Input = { folder: string; name: string }
    
    let private buildFromFolder printer input =
        let (_, err) = input.folder |> Some |> runProc printer "dotnet" "build -o ./"
        let a = err |> Seq.length 
        a |> should equal 0        
        let outFile = Path.Combine(input.folder, input.name) |> FileInfo
        outFile.Exists |> should equal true
        outFile.FullName
    
    let private printer = (fun s -> Debug.Print(s))    
    
    let nameof (q:Expr<_>) = 
          match q with 
          | DerivedPatterns.Lambdas(_, Patterns.NewUnionCase(a, b)) -> a.Name |> (fun x -> x.ToLowerInvariant())  
          | _ -> failwith "Unexpected format"
    
    let getFileFullPath tc =
        let outFile = Path.Combine(tc.folder, tc.name) |> FileInfo
        outFile.Exists |> should equal true
        outFile.FullName
    
    let getDirectoryFullPath tc = DirectoryInfo(tc.folder).FullName
    
    let fsTc() = { folder = "TestFs"; name = "TestFs.dll" }
    let csTc() = { folder = "TestCs"; name = "TestCs.dll" }
 
type BaseTestCase(generator : obj array seq) =
    interface obj array seq with
        member this.GetEnumerator() = generator.GetEnumerator()
        member this.GetEnumerator() = 
            generator.GetEnumerator() :> System.Collections.IEnumerator

type TestCases() =
    inherit BaseTestCase(seq {   
        yield [| fsTc() |]                        
        yield [| csTc() |]                        
    })

type DescribeIntegrationTests() =                
    let describe = nameof <@ Args.Describe @>     
    let fileNotEmpty = File.ReadAllText >> should not' (be NullOrEmptyString)
    let logLevel = new LoggingLevelSwitch(LogEventLevel.Debug)
    let mainc argv =
        let prepend a b = Array.append b a
        describe 
        |> Array.singleton
        |> prepend argv
        |> Program.mainUnsafeWith (fun l -> logLevel.MinimumLevel <- l)
    
    static member BinaryTestCases 
        with get() : obj array seq = 
            [|
                [| fsTc() :> obj |]                  
                [| csTc() :> obj |]
            |] |> Array.toSeq
        
        
    member this.CreateOutFileName(file: string, [<CallerMemberName>] ?memberName: string) =
        let replace (o: char) n (str: string) = str.Replace(o, n)
        match memberName with
        | Some teseCaseName ->            
            (
                teseCaseName |> replace ' ' '_',
                file |> Path.GetFileNameWithoutExtension
            )            
            ||> sprintf "%s_%s.txt"  
        | _ -> failwith "WTF"
                    
    [<Theory>]
    [<MemberData("BinaryTestCases")>]    
    member this.``binary with output`` binary =
        let file = binary |> getFileFullPath
        let resultFile = this.CreateOutFileName(file)
        [| 
           file
           "-o"; resultFile
        |] |> mainc |> fileNotEmpty
            
    [<Theory>]
    [<ClassData(typeof<TestCases>)>]    
    member this.``binary without output`` tc = tc |> getFileFullPath |> Array.singleton |> mainc |> fileNotEmpty
            
                  
    [<Theory(Skip="Not impl")>]
    [<ClassData(typeof<TestCases>)>]      
    member this.``no input`` folder =
        let cd = Directory.GetCurrentDirectory()
        try     
            folder |> Directory.SetCurrentDirectory
            mainc [| |] |> fileNotEmpty
        finally 
            Directory.SetCurrentDirectory cd 
                            
    [<Fact>]
    member this.``bad file`` () =
        let input = "BadInput.json"
        let act: Action = new Action(fun () -> mainc [|input|] |> ignore)
        Assert.Throws(typeof<BadImageFormatException>, act)     
                               
    [<Theory(Skip = "Not implemented")>]
    [<InlineData("-v")>]
    [<InlineData("--verbose")>]
    member this.``verbose log level`` verboseFlag =        
        let act: Action = new Action(fun () -> mainc [| verboseFlag |] |> ignore)
        Assert.Throws(typeof<BadImageFormatException>, act)
        
    [<Fact>]
    member this.``quiet log level`` () =
        let input = "BadInput.json"
        let act: Action = new Action(fun () -> mainc [|input|] |> ignore)
        Assert.Throws(typeof<BadImageFormatException>, act)

type DotApiIntegrationTests() =    
    let mainc = Program.mainUnsafeWith ignore
    
    [<Fact>]
    member this.``version`` () =
        let result = 
            "version"
            |> Array.singleton
            |> mainc
            
        result |> should not' (be NullOrEmptyString)
        result |> should not' (equal "0.0.0.0")
        