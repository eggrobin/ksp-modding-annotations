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

namespace 注 {

internal class L10NQuickInfoSource : IAsyncQuickInfoSource {
  public void Dispose() {
    if (!is_disposed_) {
      --instance_count_;
      GC.SuppressFinalize(this);
      is_disposed_ = true;
    }
  }

  public Task<QuickInfoItem> GetQuickInfoItemAsync(
      IAsyncQuickInfoSession session,
      CancellationToken cancellationToken) {
    // Map the trigger point down to our buffer.
    SnapshotPoint? subjectTriggerPoint =
        session.GetTriggerPoint(subject_buffer_.CurrentSnapshot);
    if (!subjectTriggerPoint.HasValue) {
      return Task.FromResult<QuickInfoItem>(null);
    }

    ITextSnapshot currentSnapshot = subjectTriggerPoint.Value.Snapshot;
    ITextSnapshotLine line = subjectTriggerPoint.Value.GetContainingLine();
    SnapshotSpan text_span = line.Extent;
    string searchText = text_span.GetText();
    return initialization_.ContinueWith((_) => {
      lock (phrasebook_.phrases) {
        foreach (string key in phrasebook_.phrases.Keys) {
          for (int foundIndex = 0; foundIndex < key.Length; foundIndex += key.Length) {
            foundIndex = searchText.IndexOf(key, foundIndex);
            if (foundIndex == -1) {
              break;
            }
            var key_span = currentSnapshot.CreateTrackingSpan(
                text_span.Start + foundIndex,
                key.Length,
                SpanTrackingMode.EdgeInclusive);
            if (key_span.GetSpan(currentSnapshot).
                Contains(subjectTriggerPoint.Value)) {
              phrasebook_.phrases.TryGetValue(key, out var phrase);
              if (phrase != null) {
                return new QuickInfoItem(key_span, phrase.Info(provider_, $"{instance_count_} instances"));
              }
            }
          }
        }
      }
      return null;
    });
  }

  internal L10NQuickInfoSource(L10NQuickInfoSourceProvider provider,
                               ITextBuffer buffer) {
    ++instance_count_;
    provider_ = provider;
    subject_buffer_ = buffer;
    Exception err = null;
    initialization_ = provider.SolutionFileEnumeratorFactory.GetListAsync(
        includeMiscellaneousProject:false,
        includeHiddenItems:false,
        includeExternalItems:false).ContinueWith((files) => {
      try {
        foreach (var file in files.Result) {
          if (file.FullPath.EndsWith(".cfg")) {
            phrasebook_.AddFile(file.FullPath);
          }
        }
      } catch (Exception e) {
        err = e;
      }
    });
  }

  private Task initialization_;
  private L10NQuickInfoSourceProvider provider_;
  private ITextBuffer subject_buffer_;
  private static Phrasebook phrasebook_ = new Phrasebook();
  private static int instance_count_ = 0;
  private bool is_disposed_;
}

}