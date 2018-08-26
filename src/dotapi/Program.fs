// Learn more about F# at http://fsharp.org

open dotapi
open Argu
open Serilog
open Serilog.Events
open System
open Argu
open Serilog.Core
open Serilog.Events


      
let CreateLogger() =
    let lvlSwitch = new LoggingLevelSwitch(LogEventLevel.Debug)
    Log.Logger <- 
        (LoggerConfiguration())
            .MinimumLevel.ControlledBy(lvlSwitch)
            .Enrich.WithDemystifiedStackTraces()
            .WriteTo.Console(outputTemplate = "{Level:u3} {Message:lj}{NewLine}{Exception}", theme = Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code)
            .CreateLogger()
    fun lvl -> lvlSwitch.MinimumLevel <- lvl


let parser = ArgumentParser.Create<Args>(programName = "dotapi")

let mainUnsafeWith loggerFactory argv = 
    let parseResult = parser.Parse(argv, raiseOnUsage = true)

        
    if parseResult.Contains <@ Args.Quiet @> then 
        loggerFactory LogEventLevel.Error       
            
    elif parseResult.Contains <@ Args.Verbose @> then 
        loggerFactory LogEventLevel.Verbose
        Log.Verbose("Log Level Set")
    
    Api.run parseResult

[<EntryPoint>]
let main argv =
    let logSwitcher = CreateLogger()
    try         
        let result = argv |> mainUnsafeWith logSwitcher
        printf "%s" result     
        0
    with 
    | :? ArguParseException as parseEx -> Log.Fatal(sprintf "Bad input \n%s" parseEx.Message); -1 
    | _ as ex -> Log.Fatal(ex, "Main"); -2 
