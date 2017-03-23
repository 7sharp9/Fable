[<Util.Testing.TestFixture>]
module Fable.Tests.Async
open System
open Util.Testing
open Fable.Tests.Util

[<Test>]
let ``Simple async translates without exception``() =
    async { return () }
    |> Async.StartImmediate

[<Test>]
let ``Async while binding works correctly``() =
    let mutable result = 0
    async {
        while result < 10 do
            result <- result + 1
    } |> Async.StartImmediate
    equal result 10

[<Test>]
let ``Async for binding works correctly``() =
    let inputs = [|1; 2; 3|]
    let result = ref 0
    async {
        for inp in inputs do
            result := !result + inp
    } |> Async.StartImmediate
    equal !result 6

[<Test>]
let ``Async exceptions are handled correctly``() =
    let result = ref 0
    let f shouldThrow =
        async {
            try
                if shouldThrow then failwith "boom!"
                else result := 12
            with _ -> result := 10
        } |> Async.StartImmediate
        !result
    f true + f false |> equal 22

[<Test>]
let ``Simple async is executed correctly``() =
    let result = ref false
    let x = async { return 99 }
    async {
        let! x = x
        let y = 99
        result := x = y
    }
    //TODO: RunSynchronously would make more sense here but in JS I think this will be ok.
    |> Async.StartImmediate
    equal !result true

type DisposableAction(f) =
    interface IDisposable with
        member __.Dispose() = f()

[<Test>]
let ``async use statements should dispose of resources when they go out of scope``() =
    let isDisposed = ref false
    let step1ok = ref false
    let step2ok = ref false
    let resource = async {
        return new DisposableAction(fun () -> isDisposed := true)
    }
    async {
        use! r = resource
        step1ok := not !isDisposed
    }
    //TODO: RunSynchronously would make more sense here but in JS I think this will be ok.
    |> Async.StartImmediate
    step2ok := !isDisposed
    (!step1ok && !step2ok) |> equal true

[<Test>]
let ``Try ... with ... expressions inside async expressions work the same``() =
    let result = ref ""
    let throw() : unit =
        raise(exn "Boo!")
    let append(x) =
        result := !result + x
    let innerAsync() =
        async {
            append "b"
            try append "c"
                throw()
                append "1"
            with _ -> append "d"
            append "e"
        }
    async {
        append "a"
        try do! innerAsync()
        with _ -> append "2"
        append "f"
    } |> Async.StartImmediate
    equal !result "abcdef"

[<Test>]
let ``Async cancellation works``() =
    async {
        let sleepAndAssign token res =
            Async.StartImmediate(async {
                do! Async.Sleep 100
                res := true
            }, token)
        let res1, res2, res3 = ref false, ref false, ref false
        let tcs1 = new System.Threading.CancellationTokenSource(50)
        let tcs2 = new System.Threading.CancellationTokenSource()
        let tcs3 = new System.Threading.CancellationTokenSource()
        sleepAndAssign tcs1.Token res1
        sleepAndAssign tcs2.Token res2
        sleepAndAssign tcs3.Token res3
        tcs2.Cancel()
        tcs3.CancelAfter(150)
        do! Async.Sleep 125
        equal false !res1
        equal false !res2
        equal true !res3
    } |> Async.RunSynchronously

let successWork: Async<string> = Async.FromContinuations(fun (onSuccess,_,_) -> onSuccess "success")
let errorWork: Async<string> = Async.FromContinuations(fun (_,onError,_) -> onError (exn "error"))
let cancelWork: Async<string> = Async.FromContinuations(fun (_,_,onCancel) ->
        System.OperationCanceledException("cancelled") |> onCancel)

[<Test>]
let ``Async.StartWithContinuations works``() =
    let res1, res2, res3 = ref "", ref "", ref ""
    Async.StartWithContinuations(successWork, (fun x -> res1 := x), ignore, ignore)
    Async.StartWithContinuations(errorWork, ignore, (fun x -> res2 := x.Message), ignore)
    Async.StartWithContinuations(cancelWork, ignore, ignore, (fun x -> res3 := x.Message))
    equal "success" !res1
    equal "error" !res2
    equal "cancelled" !res3

[<Test>]
let ``Async.Catch works``() =
    let assign res = function
        | Choice1Of2 msg -> res := msg
        | Choice2Of2 (ex: Exception) -> res := "ERROR: " + ex.Message
    let res1 = ref ""
    let res2 = ref ""
    async {
        let! x1 = successWork |> Async.Catch
        assign res1 x1
        let! x2 = errorWork |> Async.Catch
        assign res2 x2
    } |> Async.StartImmediate
    equal "success" !res1
    equal "ERROR: error" !res2

[<Test>]
let ``Async.Ignore works``() =
    let res = ref false
    async {
        do! successWork |> Async.Ignore
        res := true
    } |> Async.StartImmediate
    equal true !res

// [<Test>]
// let ``Async.OnCancel works``() =
//     async {
//         let res = ref false
//         let tcs = new System.Threading.CancellationTokenSource()
//         Async.StartImmediate(async {
//             let! _ = Async.OnCancel(fun () -> res := true)
//             do! Async.Sleep 100
//         }, tcs.Token)
//         tcs.CancelAfter 50
//         do! Async.Sleep 75
//         equal true !res
//     } |> Async.RunSynchronously

[<Test>]
let ``Async.Parallel works``() =
    async {
        let makeWork i =
            async {
                do! Async.Sleep 50
                return i
            }
        let res: int[] ref = ref [||]
        let works = [makeWork 1; makeWork 2; makeWork 3]
        async {
            let! x = Async.Parallel works
            res := x
        } |> Async.StartImmediate
        do! Async.Sleep 100
        !res |> Array.sum |> equal 6
    } |> Async.RunSynchronously

#if FABLE_COMPILER
open Fable.Core

[<Test>]
let ``Interaction between Async and Promise works``() =
    async {
        let res = ref false
        async { res := true }
        |> Async.StartAsPromise
        |> Async.AwaitPromise
        |> Async.StartImmediate
        equal true !res
    } |> Async.RunSynchronously

[<Test>]
let ``Promises can be cancelled``() =
    async {
        let res = ref 0
        let tcs = new System.Threading.CancellationTokenSource(50)
        let work =
            let work = async {
                do! Async.Sleep 75
                res := -1
            }
            Async.StartAsPromise(work, tcs.Token) |> Async.AwaitPromise
        // behavior change: a cancelled task is now triggering the exception continuation instead of
        // the cancellation continuation, see: https://github.com/Microsoft/visualfsharp/issues/1416
        // also, System.OperationCanceledException will be changed to TaskCanceledException (not yet)
        Async.StartWithContinuations(work, ignore, ignore, (fun _ -> res := 1))
        do! Async.Sleep 100
        equal 1 !res
    } |> Async.RunSynchronously
#endif

[<Test>]
let ``MailboxProcessor.post works``() =
    async {
        let mutable res = None
        let agent = new MailboxProcessor<int>(fun inbox ->
            let rec messageLoop() = async {
                let! msg = inbox.Receive()
                match msg with
                | 3 -> () // Finish loop
                | _ ->
                    res <- Some msg
                    return! messageLoop()
            }
            messageLoop()
        )
        agent.Post(1)
        equal None res // Mailbox hasn't started yet
        agent.Start()
        agent.Post(2)
        // Not necessary in the JS implementation, but in .NET
        // MailboxProcessor works in the background so we must wait a bit
        do! Async.Sleep 100
        equal (Some 2) res
        agent.Post(3)
        equal (Some 2) res  // Mailbox has finished
    } |> Async.RunSynchronously

type Message = string * AsyncReplyChannel<string>

[<Test>]
let ``MailboxProcessor.postAndAsyncReply works``() =
    async {
        let formatString = "Msg: {0} - {1}"
        let agent = MailboxProcessor<Message>.Start(fun inbox ->
            let rec loop n = async {
                let! (message, replyChannel) = inbox.Receive()
                do! Async.Sleep(100) // Delay a bit
                replyChannel.Reply(String.Format(formatString, n, message))
                if message <> "Bye" then do! loop (n + 1)
            }
            loop 0)
        let! resp = agent.PostAndAsyncReply(fun replyChannel -> "Hi", replyChannel)
        equal "Msg: 0 - Hi" resp
        let! resp = agent.PostAndAsyncReply(fun replyChannel -> "Bye", replyChannel)
        equal "Msg: 1 - Bye" resp
    } |> Async.RunSynchronously

[<Test>]
let ``Async try .. with returns correctly from 'with' branch``() =
    let work = async {
        try
          failwith "testing"
          return -1
        with e ->
          return 42 }
    let mutable result = 0
    Async.StartWithContinuations(work, (fun r -> result <- r), ignore, ignore)
    equal result 42


[<Test>]
let ``Deep recursion with async doesn't cause stack overflow``() =
    async {
        let result = ref false
        let rec trampolineTest res i = async {
            if i > 100000
            then res := true
            else return! trampolineTest res (i+1)
        }
        do! trampolineTest result 0
        equal !result true
    } |> Async.RunSynchronously

[<Test>]
let ``Nested failure propagates in async expressions``() =
    async {
        let data = ref ""
        let f1 x =
            async {
                try
                    failwith "1"
                    return x
                with
                | e -> return! failwith ("2 " + e.Message)
            }
        let f2 x =
            async {
                try
                    return! f1 x
                with
                | e -> return! failwith ("3 " + e.Message)
            }
        let f() =
            async {
                try
                    let! y = f2 4
                    return ()
                with
                | e -> data := e.Message
            }
            |> Async.StartImmediate
        f()
        do! Async.Sleep 100
        equal "3 2 1" !data
    } |> Async.RunSynchronously

[<Test>]
let ``Try .. finally expressions inside async expressions work``() =
    async {
        let data = ref ""
        async {
            try data := !data + "1 "
            finally data := !data + "2 "
        } |> Async.StartImmediate
        async {
            try
                try failwith "boom!"
                finally data := !data + "3"
            with _ -> ()
        } |> Async.StartImmediate
        do! Async.Sleep 100
        equal "1 2 3" !data
    } |> Async.RunSynchronously

[<Test>]
let ``Final statement inside async expressions can throw``() =
    async {
        let data = ref ""
        let f() = async {
            try data := !data + "1 "
            finally failwith "boom!"
        }
        async {
            try
                do! f()
                return ()
            with
            | e -> data := !data + e.Message
        }
        |> Async.StartImmediate
        do! Async.Sleep 100
        equal "1 boom!" !data
    } |> Async.RunSynchronously

[<Test>]
let ``Async.Bind propagates exceptions``() = // See #724
    async {
        let task1 name = async {
            // printfn "testing with %s" name
            if name = "fail" then
                failwith "Invalid access credentials"
            return "Ok"
        }

        let task2 name = async {
            // printfn "testing with %s" name
            do! Async.Sleep 100 //difference between task1 and task2
            if name = "fail" then
                failwith "Invalid access credentials"
            return "Ok"
        }

        let doWork name task =
            let catch comp = async {
                let! res = Async.Catch comp
                return
                    match res with
                    | Choice1Of2 str -> str
                    | Choice2Of2 ex -> ex.Message
            }
            // printfn "doing work - %s" name
            async {
                let! a = task "work" |> catch
                // printfn "work - %A" a
                let! b = task "fail" |> catch
                // printfn "fail - %A" b
                return a, b
            }

        let! res1 = doWork "task1" task1
        let! res2 = doWork "task2" task2
        equal ("Ok", "Invalid access credentials") res1
        equal ("Ok", "Invalid access credentials") res2
    } |> Async.RunSynchronously
