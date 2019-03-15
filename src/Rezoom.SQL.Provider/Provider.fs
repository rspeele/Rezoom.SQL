﻿namespace Rezoom.SQL.Provider
open System.IO
open System.Reflection
open Microsoft.FSharp.Core.CompilerServices
open ProviderImplementation.ProvidedTypes
open Rezoom.SQL.Provider.TypeGeneration
open System

[<TypeProvider>]
type public Provider(cfg : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces(cfg)

    // Get the assembly and namespace used to house the provided types.
    let thisAssembly = Assembly.GetExecutingAssembly () // Assembly.LoadFrom(cfg.RuntimeAssembly)
    let rootNamespace = "Rezoom.SQL"

    let modelCache = new UserModelCache()
    let generateType typeName model case =
        let tmpAssembly = ProvidedAssembly()
        let model = modelCache.Load(cfg.ResolutionFolder, model)
        let ty =
            {   Assembly = tmpAssembly
                Namespace = rootNamespace
                TypeName = typeName
                UserModel = model
                Case = case
            } |> generateType
        tmpAssembly.AddTypes([ ty ])
        ty

    let sqlTy =
        let sqlTy =
            ProvidedTypeDefinition(thisAssembly, rootNamespace, "SQL", Some typeof<obj>, isErased = false)
        let staticParams =
            [   ProvidedStaticParameter("sql", typeof<string>)
                ProvidedStaticParameter("model", typeof<string>, "")
            ]
        let buildSQLFromStaticParams typeName (parameterValues : obj array) =
            match parameterValues with 
            | [| :? string as sql; :? string as model |] -> generateType typeName model (GenerateSQL sql)
            | _ -> failwith "Invalid parameters (expected 2 strings: sql, model)"
        sqlTy.DefineStaticParameters(staticParams, buildSQLFromStaticParams)
        sqlTy

    let modelTy =
        let modelTy =
            ProvidedTypeDefinition(thisAssembly, rootNamespace, "SQLModel", Some typeof<obj>, isErased = false)
        let staticParams = [ ProvidedStaticParameter("model", typeof<string>, "") ]
        let buildModelFromStaticParams typeName (parameterValues : obj array) =
            match parameterValues with
            | [| :? string as model |] -> generateType typeName model GenerateModel
            | _ -> failwith "Invalid parameters (expected 1 string: model)"
        modelTy.DefineStaticParameters(staticParams, buildModelFromStaticParams)
        modelTy

    let assemblies =
      let alts = 
        [ Path.DirectorySeparatorChar
          Path.AltDirectorySeparatorChar ]
        |> List.map (fun x -> sprintf "%cref%c" x x, sprintf "%clib%c" x x)
      cfg.ReferencedAssemblies
      |> Seq.choose (fun asm ->
        AssemblyResolver.log asm
        try asm |> (File.ReadAllBytes >> Assembly.Load >> Some)
        with 
        | :? BadImageFormatException as e ->
          //hack to point to the lib dir if it is using ref
          let file =
            alts
            |> List.tryFind (fun (l, _) -> asm.IndexOf(l, StringComparison.OrdinalIgnoreCase) > -1) //? this case sensitivity should should not affect non-windows oses, right?
            |> Option.map (fun (l, r) -> asm.Replace(l, r))
            |> Option.filter System.IO.File.Exists
          match file with
          | None ->
            //File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "Alt not found: %s" asm])
            None
          | Some file ->
            try file |> (File.ReadAllBytes >> Assembly.Load >> Some)
            with | e ->
              //File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "Alt found. Load failed: %s > %A" file e])
              None
        | _ -> 
          //File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "Load failed: %s" asm]) 
          None)
      |> Array.ofSeq

    do
        let tys = [ sqlTy; modelTy ]
        this.AddNamespace(rootNamespace, tys)
        modelCache.Invalidated.Add(fun _ -> this.Invalidate())
        this.Disposing.Add(fun _ -> modelCache.Dispose())
        //File.AppendAllLines (@"c:\temp\rezoom.txt", ["ready"]) 
        //for x in cfg.ReferencedAssemblies do File.AppendAllLines (@"c:\temp\rezoom.txt", [x]) 

    override __.ResolveAssembly args =   
        //File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "in resolve assembly: %s" args.Name]) 
        let name = AssemblyName args.Name
        let existingAssembly =
            System.AppDomain.CurrentDomain.GetAssemblies ()
            |> Seq.tryFind (fun x -> AssemblyName.ReferenceMatchesDefinition (name, x.GetName()))
            |> function
              | None -> 
                assemblies
                |> Seq.tryFind (fun x -> AssemblyName.ReferenceMatchesDefinition (name, x.GetName()))
              | x -> x
        match existingAssembly with
        | Some x -> x
        | None ->
          //match AssemblyResolver.tryLoadFromLibFolder args.Name with
          //| Some x -> x
          //| _ ->
              //File.AppendAllLines (@"c:\temp\rezoom.txt", ["Not found. Using Resolver"])
              match AssemblyResolver.resolve args.Name with
              | Some x -> x
              | _ -> 
                  
                  //AssemblyResolver.log (sprintf "Still not found. Checking %s" args.Name)
                  //let n = AssemblyName args.Name
                  //if (args.Name.Contains "ConfigurationManager") then
                  //  File.AppendAllLines (@"c:\temp\rezoom.txt", ["----start----"])
                  //  File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "checking: %s" args.Name])
                  //  //search all: 
                  //  let fromCurrentDomain = System.AppDomain.CurrentDomain.GetAssemblies () |> Seq.tryFind (fun x -> x.GetName().Name = n.Name)
                  //  let fromAssemblies = assemblies |> Seq.tryFind (fun x -> x.GetName().Name = n.Name)
                  //  let fromFile =
                  //    cfg.ReferencedAssemblies
                  //    |> Array.tryFind (fun x -> x.Contains n.Name)
                  //    |> Option.bind (fun x ->
                  //      File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "Got: %s" x])
                  //      try Some <| Assembly.LoadFile x
                  //      with | e ->
                  //        File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "Error: %s > %A" x e])
                  //        None)
                  //  let gotIt = function
                  //    | None -> sprintf "%s Nope"
                  //    | Some x -> sprintf "%s Yep"
                  //  [ "Domain", fromCurrentDomain
                  //    "Assemblies", fromAssemblies
                  //    "File", fromFile ]
                  //  |> List.map (fun (name, value) -> gotIt value name)
                  //  |> List.iter (fun x -> File.AppendAllLines (@"c:\temp\rezoom.txt", [x]))
                  //  File.AppendAllLines (@"c:\temp\rezoom.txt", ["----end----"])
                  //else 
                  //  File.AppendAllLines (@"c:\temp\rezoom.txt", [sprintf "Not found: %s" n.Name])
                  base.ResolveAssembly args

[<TypeProviderAssembly>]
do ()