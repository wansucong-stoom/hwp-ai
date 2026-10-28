using System;
using System.Runtime.CompilerServices;
using Hnc.Foundation.Interface;
using Hnc.Foundation.InternalInterface;

namespace HAddon {
 // The add-on's IHncFrameEngine is shared by document windows. A route must
 // retain BOTH its engine identity and the frame ID captured at the UI entry.
 // Never recapture a queued request's route after a popup changes focus.
 internal sealed class FrameRoute : IEquatable<FrameRoute> {
  internal readonly IHncFrameEngine Engine;
  internal readonly string FrameId;

  FrameRoute(IHncFrameEngine engine, string frameId) {
   if (engine == null) throw new ArgumentNullException("engine");
   if (String.IsNullOrWhiteSpace(frameId))
    throw new InvalidOperationException("요청할 한글 문서 창을 확인할 수 없습니다.");
   Engine = engine;
   FrameId = frameId;
  }

  // Actor/popup entry: read once while the intended HWP frame is current.
  internal static FrameRoute Capture(IHncFrameEngine engine) {
   if (engine == null) throw new ArgumentNullException("engine");
   var internalEngine = engine as IHncInternalFrameEngine;
   if (internalEngine == null)
    throw new InvalidOperationException("이 한글 연결에서는 문서 창 ID를 제공하지 않습니다.");
   return new FrameRoute(engine, internalEngine.CurrentFrameId);
  }

  // Workspace registration may already own a frame ID captured on its first
  // Loaded event. Rebind that stored identity; do not replace it with whichever
  // document happens to be active during Unloaded, cancellation, or shutdown.
  internal static FrameRoute Bind(IHncFrameEngine engine, string ownerFrameId) {
   return new FrameRoute(engine, ownerFrameId);
  }

  internal bool IsOwnedBy(IHncFrameEngine owner) {
   return Object.ReferenceEquals(Engine, owner);
  }

  internal bool IsCurrent {
   get {
    try {
     var internalEngine = Engine as IHncInternalFrameEngine;
     return internalEngine != null &&
      String.Equals(FrameId, internalEngine.CurrentFrameId, StringComparison.Ordinal);
    } catch { return false; }
   }
  }

  internal void RequireCurrent() {
   if (!IsCurrent)
    throw new InvalidOperationException("요청을 시작한 한글 문서 창이 바뀌었습니다. 원래 창에서 다시 요청하세요.");
  }

  public bool Equals(FrameRoute other) {
   return other != null && Object.ReferenceEquals(Engine, other.Engine) &&
    String.Equals(FrameId, other.FrameId, StringComparison.Ordinal);
  }
  public override bool Equals(object obj) { return Equals(obj as FrameRoute); }
  public override int GetHashCode() {
   unchecked { return (RuntimeHelpers.GetHashCode(Engine) * 397) ^ StringComparer.Ordinal.GetHashCode(FrameId); }
  }
 }
}
