module AsyncBehavior

open System
open System.Threading.Channels
open System.Threading.Tasks

open NUnit.Framework

let asyncFailWithDivision () = backgroundTask {
    do! Task.Delay(10)
    raise (DivideByZeroException("Called asyncFailWithDivision"))
}

let asyncFailWithCancellation () = backgroundTask {
    do! Task.Delay(10)
    raise (OperationCanceledException("Called asyncFailWithCancellation"))
}

let asyncAssertRaises<'E> (task : Task) = backgroundTask {
    let mutable raised = false
    try
        do! task
    with e ->
        if e.GetType() = typeof<'E> then
            raised <- true
        else
            failwithf "Wrong exception raise: %A" e
    if not raised then
        failwith "No exception raised"
}

let assertRaises<'E> (f : unit -> unit) : unit =
    let mutable raised = false
    try
        f ()
    with e ->
        if e.GetType() = typeof<'E> then
            raised <- true
        else
            failwithf "Wrong exception raise: %A" e
    if not raised then
        failwith "No exception raised"

// ----------------------------------------------------------------
// DeferAsync
// ----------------------------------------------------------------

type DeferAsync(f : unit -> Task) =
    interface IAsyncDisposable with
        override _.DisposeAsync() = ValueTask(f ())

// In the following test `Task` called `task` completes AFTER `DisposeAsync` finishes.
[<Test>]
let ``task finishes after DisposeAsync completes`` () =
    backgroundTask {
        let task = backgroundTask {
            use _ = DeferAsync(fun () -> backgroundTask {
                do! Task.Delay(900)
                Console.WriteLine("From DisposeAsync (3)")
            })
            Console.WriteLine("Before exception (1)")
            do! asyncFailWithDivision ()
            Console.WriteLine("After exception (NEVER)")
        }
        do! Task.Delay(50)  // Give `task` some time so it can
        Console.WriteLine("Before task (2)")
        do! task
        Console.WriteLine("After task (NEVER)")
    } |> asyncAssertRaises<DivideByZeroException>

// ----------------------------------------------------------------
// ChannelReader
// ----------------------------------------------------------------

// Reading from empty completed channel.
// - If a channel was completed by `OperationCanceledException` or its child
//   then reading raises `TaskCanceledException`.
// - Otherwise reading raises `ChannelClosedException`.
[<Test>]
let ``reading from empty channel raises ChannelClosedException or TaskCanceledException`` () =
    backgroundTask {
        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(DivideByZeroException())
        do! ch.Reader.ReadAsync().AsTask() |> asyncAssertRaises<ChannelClosedException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(OperationCanceledException())
        do! ch.Reader.ReadAsync().AsTask() |> asyncAssertRaises<TaskCanceledException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete()
        do! ch.Reader.ReadAsync().AsTask() |> asyncAssertRaises<ChannelClosedException>
    }

// Waiting to read from empty completed channel.
// - Returns false if channel was completed without exception.
// - Returns same exception which was used to complete channel.
[<Test>]
let ``wait for read from empty channel raises ChannelClosedException or TaskCanceledException`` () =
    backgroundTask {
        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(DivideByZeroException())
        do! ch.Reader.WaitToReadAsync().AsTask() |> asyncAssertRaises<DivideByZeroException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(OperationCanceledException())
        do! ch.Reader.WaitToReadAsync().AsTask() |> asyncAssertRaises<OperationCanceledException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete()
        let! ready = ch.Reader.WaitToReadAsync()
        Assert.False(ready)
    }

// Trying to read from empty completed channel.
// Returns false no matter how channel was completed.
[<Test>]
let ``trying to read from empty channel returns false`` () =
    let ch = Channel.CreateBounded(100)
    ch.Writer.Complete(DivideByZeroException())
    let read, _ = ch.Reader.TryRead()
    Assert.False(read)

    let ch = Channel.CreateBounded(100)
    ch.Writer.Complete(OperationCanceledException())
    let read, _ = ch.Reader.TryRead()
    Assert.False(read)

    let ch = Channel.CreateBounded(100)
    ch.Writer.Complete()
    let read, _ = ch.Reader.TryRead()
    Assert.False(read)

// ----------------------------------------------------------------
// ChannelWriter
// ----------------------------------------------------------------

// Writing to completed channel.
// - If a channel was completed by `OperationCanceledException` or its child
//   then reading raises the original exception.
//   Note that this is different from `ReadAsync` which always raises `TaskCanceledException`.
// - Otherwise reading raises `ChannelClosedException`.
[<Test>]
let ``writing to completed channel raises ChannelClosedException or original exception`` () =
    backgroundTask {
        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(DivideByZeroException())
        do! ch.Writer.WriteAsync(1).AsTask() |> asyncAssertRaises<ChannelClosedException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(OperationCanceledException())
        do! ch.Writer.WriteAsync(1).AsTask() |> asyncAssertRaises<OperationCanceledException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(TaskCanceledException())
        do! ch.Writer.WriteAsync(1).AsTask() |> asyncAssertRaises<TaskCanceledException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete()
        do! ch.Writer.WriteAsync(1).AsTask() |> asyncAssertRaises<ChannelClosedException>
    }

// Waiting to write to completed channel.
// - Returns false if channel was completed without exception.
// - Returns same exception which was used to complete channel.
// Note that this is consistent with
[<Test>]
let ``wait for write to completed channel raises ChannelClosedException or TaskCanceledException`` () =
    backgroundTask {
        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(DivideByZeroException())
        do! ch.Writer.WaitToWriteAsync().AsTask() |> asyncAssertRaises<DivideByZeroException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete(OperationCanceledException())
        do! ch.Writer.WaitToWriteAsync().AsTask() |> asyncAssertRaises<OperationCanceledException>

        let ch = Channel.CreateBounded(100)
        ch.Writer.Complete()
        let! ready = ch.Writer.WaitToWriteAsync()
        Assert.False(ready)
    }

// Trying to write to completed channel.
// Returns false no matter how channel was completed.
[<Test>]
let ``trying to write to completed channel returns false`` () =
    let ch = Channel.CreateBounded(100)
    ch.Writer.Complete(DivideByZeroException())
    let written = ch.Writer.TryWrite(1)
    Assert.False(written)

    let ch = Channel.CreateBounded(100)
    ch.Writer.Complete(OperationCanceledException())
    let written = ch.Writer.TryWrite(1)
    Assert.False(written)

    let ch = Channel.CreateBounded(100)
    ch.Writer.Complete()
    let written = ch.Writer.TryWrite(1)
    Assert.False(written)

// ----------------------------------------------------------------
// Task.WhenAll, Task.WhenAny
// ----------------------------------------------------------------

// If there's no exception when `WhenAll` returns normally.
// If some task ends with exception which is not a subtype of `OperationCanceledException`
// then `WhenAll` returns exception from the leftmost such task.
// If all exceptions are subtypes of `OperationCanceledException`
// then `WhenAll` returns exception from the leftmost cancelled task.
[<Test>]
let ``WhenAll waits for all tasks but returns only exception from the leftmost failed task`` () =
    backgroundTask {
        let foo () = backgroundTask {
            do! Task.Delay(1000)
            raise (DivideByZeroException())
        }
        let bar () = backgroundTask {
            do! Task.Delay(500)
            raise (ArgumentException())
        }
        let baz () = backgroundTask {
            do! Task.Delay(10)
            raise (OperationCanceledException())
        }
        do! Task.WhenAll(foo (), bar (), baz ()) |> asyncAssertRaises<DivideByZeroException>
        do! Task.WhenAll(Task.Delay(100), bar (), foo ()) |> asyncAssertRaises<ArgumentException>
        do! Task.WhenAll(Task.Delay(100), baz ()) |> asyncAssertRaises<OperationCanceledException>

        // Even though `OperationCanceledException` is raised before `DivideByZeroException`
        // the result is `DivideByZeroException`.
        do! Task.WhenAll(baz (), foo (), baz ()) |> asyncAssertRaises<DivideByZeroException>
    }

// Even if all tasks raise `WhenAll` returns index of the first completed task.
[<Test>]
let ``WhenAny does not raise`` () =
    backgroundTask {
        let long = backgroundTask {
            do! Task.Delay(10_000)
            raise (DivideByZeroException())
        }
        let short = backgroundTask {
            do! Task.Delay(10)
            raise (ArgumentException())
        }
        let! res = Task.WhenAny(long, short)
        Assert.AreSame(short, res)
    }

// ----------------------------------------------------------------
// Task.WaitAll, Task.WaitAny
// ----------------------------------------------------------------

// Note that `AggregateException` contains even subtypes of `OperationCanceledException`.
[<Test>]
let ``WaitAll waits for all tasks and raises aggregate exception if one fails`` () =
    let foo () = backgroundTask {
        do! Task.Delay(1000)
        raise (DivideByZeroException())
    }
    let bar () = backgroundTask {
        do! Task.Delay(500)
        raise (ArgumentException())
    }
    let baz () = backgroundTask {
        do! Task.Delay(10)
        raise (OperationCanceledException())
    }
    let e = Assert.Throws<AggregateException>(fun () -> Task.WaitAll(foo (), bar (), baz ()))
    Assert.AreEqual(3, e.InnerExceptions.Count)  // Even `OperationCanceledException` is in aggregate.

    assertRaises<AggregateException> <| fun () -> Task.WaitAll(Task.Delay(100), bar (), foo ())
    assertRaises<AggregateException> <| fun () -> Task.WaitAll(Task.Delay(100), baz ())
    assertRaises<AggregateException> <| fun () -> Task.WaitAll(baz (), foo (), baz ())

// `WaitAny` returns index of a `Task` which terminated first.
[<Test>]
let ``WaitAny does not raise`` () =
    let long = backgroundTask {
        do! Task.Delay(10_000)
        raise (DivideByZeroException())
    }
    let short = backgroundTask {
        do! Task.Delay(10)
        raise (ArgumentException())
    }
    let res = Task.WaitAny(long, short)
    Assert.AreEqual(1, res)
