#nowarn "9"
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open FSharp.Data
open CommonLib
let buffer = Array.zeroCreate<char> 128
let conversions : Map<string,bool*(unit->string)> = 
    Map.ofList 
        [
            "%",(false,(fun() -> "&#37;"))
            "time_generated",(false,(fun() -> System.DateTime.Now.ToLongTimeString()))
        ]
let dynamic(page:string) = 
    let t = if System.IO.File.Exists(page) then System.IO.File.ReadAllText(page) else sprintf "Could not find dynamic page %s<br>Generated at %%time_generated%%" page
    let d = ref false
    Map.fold(fun (acc:string) key (redo,elem:unit->string) -> let key = "%"+key+"%" in if redo then acc.Replace(key,elem()) else let v = elem() in acc.Replace(key,v)) t conversions
    //let p = HtmlDocument.Load(page)
    //for i in p.Descendants "dynamic" do
        //for j in i.InnerText().Split(' ') do 
            //match j.ToLower() with
            //|"date_generated" -> 
[<EntryPoint>]
let main argv = 
    let c = 
        Array.zip
        <| Array.map enum<Daemon> [|0..7|]
        <| [|
            for i = 0 to 7 do 
                let p = new System.IO.Pipes.NamedPipeServerStream("/tmp/" + Daemon.GetName(typeof<Daemon>,enum<Daemon> i) + "_Copelands_2016")
                p.WaitForConnection()
                yield p
            |]
        |> Map.ofArray
    let p = c.[Daemon.Page]
    flush p
    while true do 
        let b = Array.zeroCreate MAX_PACKET_SIZE
        let requestedpage = 
            let packet = read(p)
            let payload = packet.payload 
            printfn "%A" payload
            let p = System.Text.ASCIIEncoding.ASCII.GetString(payload)
            let v = p.[..p.Length-2]
            if v.EndsWith "/" then v+"index.html" else v

        printfn "%A" (System.IO.Path.GetFullPath("./pages"+requestedpage))//System.IO.File.Exists("./pages"+requestedpage))
        let verb, response = 
            if requestedpage = "/" then 
                PayloadVerb.GotPage,(using(System.IO.File.OpenRead("./pages/index.html")) HtmlDocument.Load 
                |> string 
                |> System.Text.ASCIIEncoding.ASCII.GetBytes)
            elif requestedpage = "/favicon.ico" then
                printfn "ICON"
                PayloadVerb.GotFile,System.IO.File.ReadAllBytes("./resource/favicon.ico") 
            elif requestedpage.StartsWith("/dynamic/") then 
                PayloadVerb.GotPage,("."+requestedpage
                |> dynamic
                |> System.Text.ASCIIEncoding.ASCII.GetBytes)
            elif requestedpage.StartsWith("/resource/") then 
                if System.IO.File.Exists("."+requestedpage) then
                    PayloadVerb.GotFile,System.IO.File.ReadAllBytes("."+requestedpage) 
                else 
                    PayloadVerb.Res404,[||]
            elif not(System.IO.File.Exists("./pages/"+requestedpage)) then 
                PayloadVerb.GotPage, (using(System.IO.File.OpenRead("./pages/404.html")) HtmlDocument.Load 
                |> string 
                |> System.Text.ASCIIEncoding.ASCII.GetBytes)
            else 
                PayloadVerb.GotPage,(using(System.IO.File.OpenRead("./pages/"+requestedpage)) HtmlDocument.Load
                |> string 
                |> System.Text.ASCIIEncoding.ASCII.GetBytes)
        let r = {payloadsize = response.Length;payloadverb = verb; payload = Array.append response [|0uy|]}
        write c.[Daemon.Backend] r
    0 // return an integer exit code

