[<Util.Testing.TestFixture>]
module Fable.Tests.Applicative
open System
open System.Collections.Generic
open Util.Testing
open Fable.Tests.Util

let zipUnsorted (arr1:_[]) (arr2:_[]) =
  let d1 = dict arr1
  let d2 = dict arr2
  let res = ResizeArray<_>()
  for kv1 in d1 do
    let v2 =
      if d2.ContainsKey(kv1.Key) then Some(d2.[kv1.Key])
      else None
    res.Add(kv1.Key, (Some kv1.Value, v2))
  for kv2 in d2 do
    if not (d1.ContainsKey(kv2.Key)) then
      res.Add(kv2.Key, (None, Some kv2.Value))
  Array.ofSeq res

let isSortedUsing test proj (arr:_[]) =
  let rec loop i =
    if i = arr.Length then true
    else test (proj arr.[i-1]) (proj arr.[i]) && loop (i+1)
  arr.Length = 0 || loop 1

let zipSorted (arr1:('k*'v1)[]) (arr2:('k*'v2)[]) =
  let mutable i1 = 0
  let mutable i2 = 0
  let inline (<.) (a:'k) (b:'k) = compare a b < 0
  let inline eq (a:'k) (b:'k) = compare a b = 0
  let res = ResizeArray<_>()
  while i1 < arr1.Length && i2 < arr2.Length do
    let (k1, v1), (k2, v2) = arr1.[i1], arr2.[i2]
    if eq k1 k2 then
      res.Add(k1, (Some v1, Some v2))
      i1 <- i1 + 1
      i2 <- i2 + 1
    elif k1 <. k2 then
      res.Add(k1, (Some v1, None))
      i1 <- i1 + 1
    elif k2 <. k1 then
      res.Add(k2, (None, Some v2))
      i2 <- i2 + 1
  while i1 < arr1.Length do
    let k1, v1 = arr1.[i1]
    res.Add(k1, (Some v1, None))
    i1 <- i1 + 1
  while i2 < arr2.Length do
    let k2, v2 = arr2.[i2]
    res.Add(k2, (None, Some v2))
    i2 <- i2 + 2
  Array.ofSeq res

let zipAny (arr1:('k*'v1)[]) (arr2:('k*'v2)[]) =
  let inline (<=.) (a:'k) (b:'k) = compare a b <= 0
  let inline (>=.) (a:'k) (b:'k) = compare a b >= 0
  if isSortedUsing (<=.) fst arr1 && isSortedUsing (<=.) fst arr2 then zipSorted arr1 arr2
  elif isSortedUsing (>=.) fst arr1 && isSortedUsing (>=.) fst arr2 then Array.rev (zipSorted (Array.rev arr1) (Array.rev arr2))
  else zipUnsorted arr1 arr2

type Result<'s, 'f> =
    | Ok of 's
    | Error of 'f

    static member (>>=) (r: Result<'t, 'e>, f: 't -> Result<'u, 'e>) : Result<'u, 'e> =
        match r with
        | Error e -> Error e
        | Ok v -> f v

    static member (<^>) (f: 't -> 'u, r: Result<'t, 'e>) : Result<'u, 'e> =
        r >>= (f >> Ok)

    static member (<*>) (f: 't -> 'u, r: Result<'t, 'e>) : Result<'u, 'e> =
        failwith "This shouldn't be called"

    static member (<*>) (f: Result<('t -> 'u), 'e>, r: Result<'t, 'e>) : Result<'u, 'e> =
        f >>= fun f -> f <^> r

[<Test>]
let ``Infix applicative can be generated``() =
    let r = Ok 1
    let a = Ok string
    let r' = match a <*> r with
             | Ok x -> x
             | _ -> failwith "expected Ok"
    Assert.AreEqual ("1", r' )

let inline apply (a:'a) (b:'b) =
    a <*> b

[<Test>]
let ``Infix applicative with inline functions can be generated``() =
    let r = Ok 1
    let a = Ok string
    let r' = match apply a r with
             | Ok x -> x
             | _ -> failwith "expected Ok"
    Assert.AreEqual ("1", r' )

type Foo1(i) =
    member x.Foo() = i
    member x.Foo(j) = i + j

type Foo2(i) =
    member x.Foo(j) = (i + j) * 2

let inline foo< ^t when ^t : (member Foo : int -> int)> x i =
    (^t : (member Foo : int -> int) (x, i))

[<Test>]
let ``Local inline typed lambdas work``() =
    let inline localFoo (x:^t) = foo x 5
    let x1 = Foo1(2)
    let x2 = Foo2(2)
    Assert.AreEqual(7, localFoo x1)
    Assert.AreEqual(14, localFoo x2)

[<Test>]
let ``Local inline values work``() =
    let res = zipAny [|("a",1);("b",2)|] [|("c",5.);("a",4.)|]
    res.Length |> equal 3
    res.[0] |> fst |> equal "a"
    res.[0] |> snd |> equal (Some 1, Some 4.)
    res.[1] |> fst |> equal "b"
    res.[1] |> snd |> equal (Some 2, None)
    res.[2] |> fst |> equal "c"
    res.[2] |> snd |> equal (None, Some 5.)

open Aether
open Aether.Operators

let Lens_get (g, _) = fun o -> g o
let Lens_set (_, s) = fun i o -> s i o
let Lens_map (g, s) = fun f o -> s (f (g o)) o

let chars : Isomorphism<string, char[]> =
    (fun x -> x.ToCharArray ()), (fun x -> String (x))

let rev : Isomorphism<char[], char[]> =
    Array.rev, Array.rev

let inline (=!) x y = Assert.AreEqual(y, x)

[<Test>]
let ``Lens.get returns correct values`` () =
    Lens_get fst_ ("Good","Bad") =! "Good"

[<Test>]
let ``Lens.set sets value correctly`` () =
    Lens_set fst_ "Good" ("Bad",()) =! ("Good",())

[<Test>]
let ``Lens.map modifies values correctly`` () =
    Lens_map fst_ (fun x -> x + x) ("Good",()) =! ("GoodGood",())

[<Test>]
let ``Ismorphism composition over a lens gets value`` () =
    Lens_get (fst_ >-> chars) ("Good",()) =! [| 'G'; 'o'; 'o'; 'd' |]

[<Test>]
let ``Ismorphism composition over a lens sets value`` () =
    Lens_set (fst_ >-> chars) [| 'G'; 'o'; 'o'; 'd' |] ("Bad",()) =! ("Good",())

[<Test>]
let ``Ismorphism composition over a lens gets value over multiple isomorphisms`` () =
    Lens_get (fst_ >-> chars >-> rev) ("dooG",()) =! [| 'G'; 'o'; 'o'; 'd' |]

[<Test>]
let ``Ismorphism composition over a lens sets value over multiple isomorphisms`` () =
    Lens_set (fst_ >-> chars >-> rev) [| 'd'; 'o'; 'o'; 'G' |] ("Bad",()) =! ("Good",())
