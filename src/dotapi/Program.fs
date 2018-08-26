// Learn more about F# at http://fsharp.org

open dotapi
open Argu
open System

let parser = ArgumentParser.Create<Args>(programName = "dotapi")

let mainUnsafeWith loggerFactory argv = 
    let parseResult = parser.Parse argv
    loggerFactory parseResult
    Api.run parseResult

[<EntryPoint>]
let main argv =
    let result = argv |> mainUnsafeWith Logger.CreateLogger
    printf "%s" result     
    0
