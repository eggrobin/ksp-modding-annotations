using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell.Internal.FileEnumerationService;

namespace 注 {

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("ToolTip QuickInfo Source")]
[Order(Before = "Default Quick Info Presenter")]
[ContentType("text")]
internal class L10NQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider {
  public IAsyncQuickInfoSource
      TryCreateQuickInfoSource(ITextBuffer textBuffer) {
    return new L10NQuickInfoSource(this, textBuffer);
  }

  [Import]
  internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

  [Import]
  internal ITextBufferFactoryService TextBufferFactoryService { get; set; }

  [Import(typeof(SVsServiceProvider))]
  internal IServiceProvider ServiceProvider { get; set; }

  [Import]
  internal ISolutionFileEnumeratorFactory SolutionFileEnumeratorFactory { get; set; }
}

internal class L10NQuickInfoController : IIntellisenseController {
  internal L10NQuickInfoController(ITextView textView,
                                   IList<ITextBuffer> subjectBuffers,
                                   L10NQuickInfoControllerProvider provider) {
    m_textView = textView;
    m_subjectBuffers = subjectBuffers;
    m_provider = provider;

    m_textView.MouseHover += this.OnTextViewMouseHover;
  }

  private async void
      OnTextViewMouseHover(object sender, MouseHoverEventArgs e) {
    //find the mouse position by mapping down to the subject buffer
    SnapshotPoint? point = m_textView.BufferGraph.MapDownToFirstMatch(
        new SnapshotPoint(m_textView.TextSnapshot, e.Position),
        PointTrackingMode.Positive,
        snapshot => m_subjectBuffers.Contains(snapshot.TextBuffer),
        PositionAffinity.Predecessor);

    if (point != null) {
      ITrackingPoint triggerPoint =
          point.Value.Snapshot.CreateTrackingPoint(
              point.Value.Position,
              PointTrackingMode.Positive);

      if (!m_provider.QuickInfoBroker.IsQuickInfoActive(m_textView)) {
        m_session =
            await m_provider.QuickInfoBroker.TriggerQuickInfoAsync(
                m_textView,
                triggerPoint);
      }
    }
  }

  public void Detach(ITextView textView) {
    if (m_textView == textView) {
      m_textView.MouseHover -= this.OnTextViewMouseHover;
      m_textView = null;
    }
  }

  public void ConnectSubjectBuffer(ITextBuffer subjectBuffer) {}

  public void DisconnectSubjectBuffer(ITextBuffer subjectBuffer) {}

  private ITextView m_textView;
  private IList<ITextBuffer> m_subjectBuffers;
  private L10NQuickInfoControllerProvider m_provider;
  private IAsyncQuickInfoSession m_session;
}

[Export(typeof(IIntellisenseControllerProvider))]
[Name("ToolTip QuickInfo Controller")]
[ContentType("text")]
internal class
    L10NQuickInfoControllerProvider : IIntellisenseControllerProvider {
  [Import]
  internal IAsyncQuickInfoBroker QuickInfoBroker { get; set; }

  public IIntellisenseController TryCreateIntellisenseController(
      ITextView textView,
      IList<ITextBuffer> subjectBuffers) {
    return new L10NQuickInfoController(textView, subjectBuffers, this);
  }
}

}