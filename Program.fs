#nowarn "9"
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open FSharp.Data
open CommonLib
open Timetable
let tweakables =
    {
        daysBetween = 2
        timeOff = [||]
        minimumFraction = 5
        maximumFraction = 4
        acceptableOffset = t 3 0
    }
let buffer = Array.zeroCreate<char> 128
let user_db = "/home/harlan/website_ssl/user.db"
let users = 
    try 
        System.Collections.Generic.Dictionary(
            System.IO.File.ReadAllLines(user_db)|>Seq.map(fun (i) -> let j = i.Split(',') in j.[0],j.[1])
            |> dict
        )
    with 
    | :? System.IndexOutOfRangeException -> System.Collections.Generic.Dictionary()
    |e -> failwithf "Could not open the user database:\n%A" e
let conversions : list<string*(unit->string)> = 
        [
            "%",(fun() -> "&#37;")
            "time_generated",System.DateTime.Now.ToLongTimeString
        ]
let sessions = System.Collections.Generic.Dictionary<string,string*System.DateTime>()
let dynamic (vars:(string*(unit->string)) list) session (page:string) = 
    let (a:string),(b:string option) = 
        if page="./HTML/dynamic/index.html" then
            //If this goes wrong, BAD USER!!!
            try
                let vars = Map.ofList vars
                //We have a user login
                let user = vars.["username"]()
                let pswd = users.[user]
                let pswd'= vars.["password"]()
                if pswd=pswd' then 
                    let session = System.Guid.NewGuid().ToByteArray()|>System.Convert.ToBase64String
                    sessions.Add(session,(user,System.DateTime.Now+System.TimeSpan(0,30,0)))
                    async{while System.DateTime.Now < snd(sessions.[session]) do do! Async.Sleep 3600}
                    |> Async.Catch
                    |> Async.Ignore
                    |> Async.Start
                    "<meta http-equiv='refresh' content='0; url=/dynamic/MySchedule.html'>", Some(session)
                else 
                    "<meta http-equiv='refresh' content='0; url=/Login_bad.html'>",None
            with |_ -> "<meta http-equiv='refresh' content='0; url=/Login_bad.html'>",None
        elif page="./HTML/dynamic/create/index.html" then
            try
                let vars = Map.ofList vars
                //We have a user login
                let user  = vars.["username"]()
                let pswd' = vars.["password"]()
                let pswd''= vars.["password2"]()
                let good = 
                    lock users (fun () ->
                        if pswd'<>pswd'' || users.ContainsKey(user) then false else
                            System.IO.File.AppendAllText(user_db,user+","+pswd')
                            users.Add(user,pswd')
                            true
                    )
                if good then
                    let session = System.Guid.NewGuid().ToByteArray()|>System.Convert.ToBase64String
                    sessions.Add(session,(user,System.DateTime.Now+System.TimeSpan(0,30,0)))
                    async{while System.DateTime.Now < snd(sessions.[session]) do do! Async.Sleep 3600}
                    |> Async.Catch
                    |> Async.Ignore
                    |> Async.Start
                    "<meta http-equiv='refresh' content='0; url=/dynamic/MySchedule.html'>", Some(session)
                else "<meta http-equiv='refresh' content='0; url=/SignUp_bad.html'>",None 
            with |_ -> "<meta http-equiv='refresh' content='0; url=/SignUp_bad.html'>",None
        elif page="./HTML/dynamic/generate_timetable_form/index.html" then
            try
                let vars = Map.ofList vars
                let parts = vars.["parts"]() |> int
                let days = vars.["days"]() |> int
                let subjects = vars.["subjects"]()|>int
                let form = 
                    [|
                        for i = 1 to days do
                            yield 
                                [|for j = 1 to parts do yield sprintf "<td><input type='checkbox' value='timeoff' name='%i-%i'/></td>" i j|]
                                |> String.concat " "
                                |> sprintf "<tr><th scope='row'>Day %i</th>%s</tr>" i
                    |]
                    |> Array.append [|for i = 1 to parts do yield sprintf "<th>Part %i</th>" i|]
                    |> String.concat " "
                    |> sprintf "<table><th></th>%s</table>" 

                "<html>"+
                "<head>" +
                "<meta content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=0' name='viewport' />" +
                "<meta name='viewport' content='width=device-width' />" +
                "<link href='/resources/assets/css/login.css' rel='stylesheet' />" +
                "</head>" +
                "<body class='content'>"+
                "<div class='title'>Timetable generator</div>" +
                "Select the days you have off:"+
                "<form method='post' action='/dynamic/generate_timetable/'>"+
                form +
                (sprintf "<input type='hidden' name='parts' value='%i'/>" parts) +
                (sprintf "<input type='hidden' name='days' value='%i'/>" days) +
                (sprintf "<input type='hidden' name='subjects' value='%i'/>" subjects) +
                "<br><br>"+
                "Select the length of each part:" +
                (
                    [|for i = 1 to parts do yield sprintf "<td><input type='number' value='timeoff' name='length-%i'/></td>" i|] 
                    |> String.concat " "
                    |> sprintf "<table>%s<tr>%s</tr></table>" ([|for i = 1 to parts do yield sprintf "<th>Part %i</th>" i|]|>String.concat " ")
                ) +
                "<br><br>"+
                "Select the daily breaks you have (i.e. meals):" +
                (
                    [|for i = 1 to parts do yield sprintf "<td><input type='checkbox' value='timeoff' name='%i'/></td>" i|] 
                    |> String.concat " "
                    |> sprintf "<table>%s<tr>%s</tr></table>" ([|for i = 1 to parts do yield sprintf "<th>Part %i</th>" i|]|>String.concat " ")
                ) +
                "<br><br>"+
                "Type in your subjects, in order of difficulty/importance" +
                (
                    [|for i = 1 to subjects do yield sprintf "<input type='textbox' placeholder='Subject %i' name='subject-%i'/>" i i|]
                    |> String.concat " "
                ) +
                "<button type='submit'>Go!</button>" +
                "</form>"+
                "</body>" +
                "</html>",None
            with |_ -> "<meta http-equiv='refresh' content='0; url=/GenerateTimetableForm.html'>",None
        elif page = "./HTML/dynamic/generate_timetable/index.html" then
            try 
                let vars = Map.ofList vars
                let days = vars.["days"]()|>int
                let parts = vars.["parts"]()|>int
                let n_subjects = vars.["subjects"]()|>int
                let days_off = 
                    [|
                        for day = 1 to days do 
                            for part = 1 to parts do 
                                    let v = sprintf "%i-%i" day part
                                    match vars.TryFind v with |Some(v) -> yield v |_ -> ()
                    |]
                let routine = 
                    [|
                        for part = 1 to parts do yield match string part |> vars.TryFind with |Some(v) -> Routine.Break|_ -> Routine.Work
                    |]
                let subjects = [|for i = 1 to n_subjects do yield vars.[sprintf "subject-%i" i]()|]
                let lengths = [|for i = 1 to parts do yield vars.[sprintf "length-%i" i]()|>int|]
                let request = 
                    {
                        days = days
                        subjects = subjects |> Array.mapi (fun i s -> {subject=s; difficultyPos=i})
                        tweakables = tweakables
                        routine = Array.zip routine lengths
                    }
                let timetable = createTimetable request |> Option.get
                [|
                    for (i,day) in Seq.indexed timetable do 
                        for (part,j) in day do 
                            yield
                                match part with
                                |Choice1Of2(subject) -> subject
                                |Choice2Of2(Routine.Break) -> "<i>Break</i>"
                                |Choice2Of2(Routine.Work) -> "<i>Free choice</i>"
                                |Choice2Of2(Routine.TimeOff) -> "<i>Time off</i>"
                                |_ -> raise (System.NotImplementedException())
                                |> Array.map(sprintf "<td>%s</td>")
                |]
                timetable
            with |e -> sprintf "Something went wrong with generating the timetable. If you are trying to automate this, please check your code!<br>%A" (e.GetType()),None
        else
            let t = 
                if System.IO.File.Exists(page) then System.IO.File.ReadAllText(page) 
                else sprintf "Could not find dynamic page %s<br>Generated at %%time_generated%%" page
            let d = ref false
            let vars' = 
                vars 
                @
                [
                    "verified_username", (fun () -> match session with |None -> "!no_user!" |Some(s) -> sessions.[s]|>fst) 
                ]
            List.fold(fun (acc:string) (key,elem:unit->string) -> 
                let key = "%"+key+"%"
                if acc.Contains(key) then
                    let v = elem()
                    acc.Replace(key,v)
                else acc
            ) t vars', None
    PayloadVerb.GotPage,(System.Text.ASCIIEncoding.ASCII.GetBytes a),b
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
        let packet = read(p)
        //Start the thread
        //async{
        try
            let b = Array.zeroCreate MAX_PACKET_SIZE
            let requestedpage',vars = 
                let payload = packet.payload 
                let m = 
                    conversions
                    @
                    [
                        "payload_size",(fun() -> payload.Length|>string)
                    ]
                if packet.payloadverb = PayloadVerb.GotPost then
                    let s = Array.findIndex(function |0uy -> true |_ -> false) payload
                    let path = payload.[..s-1]
                    let a = payload.[s+1..]
                    let rec getall acc i j =
                        Array.tryFindIndex((=) 0uy) i
                        |> function 
                            |None -> List.rev acc
                            |Some(s) -> let a,b = Array.splitAt s i in getall (a::acc) (b.[1..]) (j+s)
                    let a' = getall [] a 0 |> List.map (System.Text.ASCIIEncoding.ASCII.GetString) :> seq<_>
                    let e = a'.GetEnumerator()
                    System.Text.ASCIIEncoding.ASCII.GetString(path), m @ 
                        [while e.MoveNext() do 
                            yield e.Current,(e.MoveNext()|>ignore;let v = e.Current in (fun () -> v))
                        ]
                else
                    //printfn "%A" payload
                    let p = System.Text.ASCIIEncoding.ASCII.GetString(payload)
                    let v = p.[..p.Length-2]
                    v, m
            let requestedpage = 
                match Seq.tryFindIndex((=) '?') requestedpage' with
                |Some(i) -> requestedpage'.[..i-1]
                |_ -> requestedpage'
                |> (fun v -> if v.EndsWith "/" then v+"index.html" else v)
            //printfn "%A" (System.IO.Path.GetFullPath("./HTML/pages"+requestedpage))//System.IO.File.Exists("./pages"+requestedpage))
            printfn "%s[%A]" requestedpage requestedpage.Length
            let session = packet.session
            let verb, response, setsession = 
                if requestedpage = "/" then 
                    PayloadVerb.GotPage,(using(System.IO.File.OpenRead("./HTML/pages/index.html")) HtmlDocument.Load 
                    |> string 
                    |> System.Text.ASCIIEncoding.ASCII.GetBytes),None
                elif requestedpage = "/favicon.ico" then
                    printfn "ICON"
                    PayloadVerb.GotFile,System.IO.File.ReadAllBytes("./HTML/resources/favicon.ico"), None
                elif requestedpage.StartsWith("/command/") then
                    match requestedpage.Substring("/command/".Length).ToLower() with
                    |"reload" -> 
                        //let p = System.Diagnostics.Process.Start("bash", "-c \"git -C ~/RevisionHub/Page/bin/Debug/HTML pull https://github.com/RevisionHub/HTML.git master\"");
                        PayloadVerb.GotFile, System.Text.ASCIIEncoding.ASCII.GetBytes "<html>Sorry Victor. I have locked the pages to preserve some vital edits I made. Send me an email if you need it!</html>", None
                    |_ -> PayloadVerb.Res404,[||], None
                elif requestedpage.StartsWith("/dynamic/") then "./HTML"+requestedpage|> dynamic vars session
                elif requestedpage.StartsWith("/resources/") then 
                    if System.IO.File.Exists("./HTML/"+requestedpage) then
                        //printf "Getting..."
                        let v = PayloadVerb.GotFile,System.IO.File.ReadAllBytes("./HTML/"+requestedpage), None
                        //printfn " Got"
                        v
                    else 
                        printfn "Res404: %A" requestedpage
                        PayloadVerb.Res404,[||], None
                elif not(System.IO.File.Exists("./HTML/pages/"+requestedpage)) then 
                    PayloadVerb.GotPage, (using(System.IO.File.OpenRead("./HTML/pages/404.html")) HtmlDocument.Load 
                    |> string 
                    |> System.Text.ASCIIEncoding.ASCII.GetBytes), None
                else 
                    PayloadVerb.GotPage,(using(System.IO.File.OpenRead("./HTML/pages/"+requestedpage)) HtmlDocument.Load
                    |> string 
                    |> System.Text.ASCIIEncoding.ASCII.GetBytes), None
            let r = 
                {
                    payloadsize = response.Length
                    payloadverb = verb
                    session = if setsession.IsSome then setsession else session
                    payload = response
                }
            write c.[Daemon.Backend] r
        with
        |e -> 
            let p = sprintf "Well done! You crashed the Page daemon!<br>%A" (e.GetType())|> System.Text.ASCIIEncoding.ASCII.GetBytes
            write c.[Daemon.Backend] {payloadsize = p.Length; payloadverb = PayloadVerb.GotPage; session = None; payload = p}
        //}
        //|> Async.Start
    0 // return an integer exit code

