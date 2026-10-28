using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HAddon {
 // This is an experimental read-only coordinator. Each native call remains
 // on the creator's UI thread; only immutable HWPML processing runs in a
 // worker. It never leaves an InitScan open or moves the caret. A matching
 // final digest does NOT prove that there was no edit followed by undo while
 // pages were read, nor does it prove that HWP returned complete text or that
 // a page maps to an editable location. It must not authorize writes.
 internal interface IResponsivePageSource {
  string Identity();
  string ReadXml();
  long PageCount();
  string ReadPage(int zeroBasedPage,int option);
 }
 internal sealed class ResponsivePageResult {
  internal string Identity,Digest,RawXml;
  internal string[] Pages,TablePages;
  internal DocumentParagraph[] Paragraphs;
 }
 internal static class ResponsivePageReader {
  const int MaxPages=256,MaxChars=400000,MaxPageChars=30000;
  static void CheckUi(int threadId,IResponsivePageSource source,string identity){
   if(Thread.CurrentThread.ManagedThreadId!=threadId)throw new InvalidOperationException("한글 UI 스레드가 변경되었습니다.");
   if(source.Identity()!=identity)throw new InvalidOperationException("쪽 내용을 읽는 동안 문서가 바뀌었습니다.");
  }
  internal static async Task<ResponsivePageResult> ReadAsync(IResponsivePageSource source,CancellationToken cancellation,Action<int,int> progress){
   if(source==null)throw new ArgumentNullException("source");
   // Without a synchronization context the await continuation could invoke
   // HWP on a thread pool thread, which is never allowed.
   if(SynchronizationContext.Current==null)throw new InvalidOperationException("한글 UI 실행 컨텍스트를 확인하지 못했습니다.");
   int uiThread=Thread.CurrentThread.ManagedThreadId;cancellation.ThrowIfCancellationRequested();
   string identity=source.Identity();if(String.IsNullOrEmpty(identity))throw new InvalidOperationException("열린 문서를 확인하지 못했습니다.");
   string raw=source.ReadXml();long count=source.PageCount();CheckUi(uiThread,source,identity);
   if(count<1||count>MaxPages)throw new InvalidOperationException("문서 쪽 수가 분석 한도를 벗어납니다.");
   var parsed=await Task.Run(delegate{return new {Digest=TableStableSnapshot.Digest(raw),Paragraphs=DocumentStructure.Read(raw)};});
   CheckUi(uiThread,source,identity);cancellation.ThrowIfCancellationRequested();
   var pages=new List<string>();var tablePages=new List<string>();int total=0,tableTotal=0;
   for(int i=0;i<count;i++){
    CheckUi(uiThread,source,identity);cancellation.ThrowIfCancellationRequested();
    string page=source.ReadPage(i,7),table=source.ReadPage(i,1);
    CheckUi(uiThread,source,identity);
    if(page==null||table==null||page.Length>MaxPageChars||table.Length>MaxPageChars||(total+=page.Length)>MaxChars||(tableTotal+=table.Length)>MaxChars)throw new InvalidOperationException("쪽 내용을 빠짐없이 읽을 수 없습니다.");
    pages.Add(page);tablePages.Add(table);if(progress!=null)progress(i+1,(int)count);
    // A single native call may itself block. This only yields between pages;
    // it provides no real-time latency bound for HWP pagination/serialization.
    await Task.Yield();
   }
   cancellation.ThrowIfCancellationRequested();CheckUi(uiThread,source,identity);
   if(total==0&&tableTotal==0)throw new InvalidOperationException("한글에서 쪽별 텍스트를 반환하지 않았습니다.");
   if(source.PageCount()!=count)throw new InvalidOperationException("쪽 내용을 읽는 동안 쪽 수가 바뀌었습니다.");
   string after=source.ReadXml();CheckUi(uiThread,source,identity);
   string digest=await Task.Run(delegate{return TableStableSnapshot.Digest(after);});
   CheckUi(uiThread,source,identity);cancellation.ThrowIfCancellationRequested();
   if(digest!=parsed.Digest)throw new InvalidOperationException("쪽 내용을 읽는 동안 문서가 바뀌었습니다.");
   return new ResponsivePageResult{Identity=identity,Digest=parsed.Digest,RawXml=raw,Pages=pages.ToArray(),TablePages=tablePages.ToArray(),Paragraphs=parsed.Paragraphs};
  }
 }
}
