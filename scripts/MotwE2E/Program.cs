using Lhamiel.Util;
using System.Diagnostics;
using System.Text.Json;

// 失敗モード: 大きい一フォルダーの列挙で取消が遅延、取消後にも ADS を書く。
if(args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || Directory.Exists(args[0]))
    throw new ArgumentException("Unused absolute fixture root required");
var root=args[0];var flat=Path.Combine(root,"data");Directory.CreateDirectory(flat);
const int count=50000;
for(int i=0;i<count;i++) File.WriteAllBytes(Path.Combine(flat,$"canary-{i:D5}.txt"),[]);
var baseline=Stopwatch.StartNew();var paths=Directory.EnumerateFiles(flat).ToArray();baseline.Stop();
using var cancellation=new CancellationTokenSource();
long cancelledAt=0;
using var registration=cancellation.Token.Register(()=>Interlocked.Exchange(ref cancelledAt,Stopwatch.GetTimestamp()));
var operation=Stopwatch.StartNew();cancellation.CancelAfter(15);
var observed=false;
try{MotwPropagator.PropagateToDirectory(flat,"[ZoneTransfer]\nZoneId=3",cancellation.Token);}
catch(OperationCanceledException){observed=true;}
operation.Stop();
var returnedAt=Stopwatch.GetTimestamp();
var adsCount=paths.Count(p=>File.Exists(p+":Zone.Identifier"));
var passed=observed && cancelledAt!=0 && adsCount==0;
File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new {
    passed,fileCount=paths.Length,baselineEnumerationMs=baseline.Elapsed.TotalMilliseconds,
    cancellationRequestedAfterMs=15,operationMs=operation.Elapsed.TotalMilliseconds,
    returnedAfterCancellationMs=cancelledAt==0 ? (double?)null : Stopwatch.GetElapsedTime(cancelledAt,returnedAt).TotalMilliseconds,
    cancellationObserved=observed,zoneIdentifierWritten=adsCount,
    limitation="Cancellation phase is inferred from uncancelled enumeration duration and no ADS writes; no internal timing hook.",
    productSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MotwPropagator).Assembly.Location)))
},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(File.ReadAllText(Path.Combine(root,"result.json")));
return passed ? 0 : 1;
