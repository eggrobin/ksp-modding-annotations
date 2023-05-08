using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace 注 {

internal class SourceLocation {
  public string file;
  public int line;
  public int first_column;
  public int last_column;
  public DateTime last_updated;
}

internal class Version {
  public Version(string language,
                 string[] parameters,
                 bool optional,
                 IEnumerable<ClassifiedTextRun> runs,
                 SourceLocation location) {
    this.language = language;
    this.parameters = parameters;
    this.optional = optional;
    this.runs = runs;
    this.location = location;
  }

  public string language { get; }
  public string[] parameters { get; }
  public bool optional { get; }
  public IEnumerable<ClassifiedTextRun> runs { get; }

  public IEnumerable<ClassifiedTextRun> Prefix(
      L10NQuickInfoSourceProvider provider) {
    if (parameters != null) {
      yield return new ClassifiedTextRun(
          PredefinedClassificationTypeNames.Punctuation,
          "(");
      for (int i = 0; i < parameters.Length; ++i) {
        yield return new ClassifiedTextRun(
            PredefinedClassificationTypeNames.String,
            parameters[i]);
        if (i != parameters.Length - 1) {
          yield return new ClassifiedTextRun(
              PredefinedClassificationTypeNames.Punctuation,
              ",");
        }
      }
      yield return new ClassifiedTextRun(
          PredefinedClassificationTypeNames.Punctuation,
          ")");
    }
    if (optional) {
      yield return new ClassifiedTextRun(
          PredefinedClassificationTypeNames.Operator,
          ".");
    }
    yield return new ClassifiedTextRun(PredefinedClassificationTypeNames.Text,
                                       language,
                                       navigationAction: () => {
                                         VsShellUtilities.OpenDocument(
                                             provider.ServiceProvider,
                                             location.file,
                                             VSConstants.LOGVIEWID_TextView,
                                             out _,
                                             out _,
                                             out IVsWindowFrame frame,
                                             out IVsTextView view);
                                         frame.Show();
                                         view.SetCaretPos(
                                             location.line - 1,
                                             location.first_column - 1);
                                         var span = new TextSpan{
                                             iStartLine = location.line - 1,
                                             iStartIndex = location.first_column - 1,
                                             iEndLine = location.line - 1,
                                             iEndIndex = location.last_column - 1
                                         };
                                         view.EnsureSpanVisible(span);
                                         view.CenterLines(location.line - 1, 1);
                                       });
    yield return new ClassifiedTextRun(PredefinedClassificationTypeNames.Text,
                                       ": ");
  }

  public SourceLocation location { get; }
}

internal class Phrase {
  public List<Version> versions { get; } = new List<Version>();

  public ContainerElement Info(L10NQuickInfoSourceProvider provider, string debug = null) {
    return new ContainerElement(ContainerElementStyle.Stacked,
                                InfoLines(provider, debug));
  }

  private IEnumerable<ClassifiedTextElement> InfoLines(
      L10NQuickInfoSourceProvider provider, string debug) {
    foreach (var version in versions) {
      yield return new ClassifiedTextElement(
          version.Prefix(provider).Concat(version.runs)
          #if DEBUG
              .Append(
              new ClassifiedTextRun(PredefinedClassificationTypeNames.ExcludedCode,
                                    version.location.last_updated.ToString("HH:mm:ss")))
          #endif
          );
    }
    if (debug != null) {
      yield return new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, debug));
    }
  }
}

internal class Phrasebook {
  public static string[] KSPLanguages =
      new[] { "en-us", "fr-fr", "ru", "zh-cn" };

  public void AddFile(string path) {
    if (!files_.Contains(path)) {
      files_.Add(path);
      DirectoryInfo directory = Directory.GetParent(path);
      if (deepest_common_ancestor_ == null) {
        deepest_common_ancestor_ = directory;
      }
      while (!new Uri(deepest_common_ancestor_.FullName).IsBaseOf(
                new Uri(directory.FullName))) {
        deepest_common_ancestor_ = deepest_common_ancestor_.Parent;
      }
      if (watcher_ == null) {
        watcher_ = new FileSystemWatcher(deepest_common_ancestor_.FullName);
        watcher_.Renamed += RefreshRenamedFile;
        watcher_.Created += RefreshFile;
        watcher_.Changed += RefreshFile;
        watcher_.Filter = "*.cfg";
        watcher_.IncludeSubdirectories = true;
        watcher_.EnableRaisingEvents = true;
      } else {
        watcher_.Path = deepest_common_ancestor_.FullName;
      }

      AddLocalizationKeys(LexNodes(StreamLines(path)), path);
    }
  }

  private void RefreshRenamedFile(object sender, FileSystemEventArgs e) {
    lock (phrases) {
      if (!files_.Contains(e.FullPath)) {
        return;
      }
      foreach (var key_phrase in phrases) {
          Phrase phrase = key_phrase.Value;
          phrase.versions.RemoveAll(version => version.location.file == e.FullPath);
      }
    retry:
      try {
        AddLocalizationKeys(LexNodes(StreamLines(e.FullPath)), e.FullPath);
      } catch (System.IO.IOException) {
        System.Threading.Thread.Sleep(1000);
        goto retry;
      }
    }
  }

  private void RefreshFile(object sender, FileSystemEventArgs e) {
    lock (phrases) {
      foreach (var key_phrase in phrases) {
          Phrase phrase = key_phrase.Value;
          phrase.versions.RemoveAll(version => version.location.file == e.FullPath);
      }
    retry:
      try {
        AddLocalizationKeys(LexNodes(StreamLines(e.FullPath)), e.FullPath);
      } catch (System.IO.IOException) {
        System.Threading.Thread.Sleep(1000);
        goto retry;
      }
    }
  }

  private IEnumerable<string> StreamLines(string path) {
    using (var stream = new System.IO.StreamReader(path)) {
      while (stream.ReadLine() is string line) {
        yield return line;
      }
    }
  }

  struct LocatedToken {
    public int line;
    public int column;
    public string token;
  }

  private IEnumerable<LocatedToken> LexNodes(IEnumerable<string> lines) {
    int line_number = 0;
    foreach (string line in lines) {
      ++line_number;
      string no_comment =
          line.Split(new[] { "//" }, 2, StringSplitOptions.None)[0];
      int column_number = 1;
      foreach (string token in Regex.Split(no_comment, "({|})")) {
        yield return new LocatedToken
            { line = line_number, column = column_number, token = token };
        column_number += token.Length;
      }
    }
  }

  private void AddLocalizationKeys(IEnumerable<LocatedToken> parsed_nodes,
                                   string file) {
    string name = "";
    var node_stack = new List<string>();
    foreach (var located_token in parsed_nodes) {
      string token = located_token.token;
      if (token == "{") {
        node_stack.Add(name);
        name = "";
      }
      if (token == "}") {
        node_stack.RemoveAt(node_stack.Count - 1);
      }
      if (node_stack.Count > 2 ||
          node_stack.Count >= 1 && node_stack[0] != "Localization") {
        // We don’t care about what happens in the depths, or outside of a root Localization node.
        continue;
      }
      if (token.Contains('=')) {
        if (node_stack.Count == 2) {
          string language = node_stack[1];
          string[] kv = token.Split(new[] { '=' }, 2);
          string key = kv[0].Trim();
          string value = kv[1].Trim();
          Add(language,
              key,
              value,
              new SourceLocation{
                  file = file, line = located_token.line,
                  first_column = located_token.column + kv[0].Length + 1,
                  last_column = located_token.column + token.Length,
                  last_updated = DateTime.Now,
              });
        }
      } else {
        name = token.Trim();
      }
    }
  }

  public void Add(string language,
                  string key,
                  string value,
                  SourceLocation location) {
    string[] parameters = null;
    string name = key;
    string optional_suffix = $".{language}";
    bool optional = key.EndsWith(optional_suffix);
    if (optional) {
      name = name.Substring(0, name.Length - optional_suffix.Length);
      var match = Regex.Match(name, @"^([^(]*)\(([^)]*)\)$");
      if (match.Success) {
        name = match.Groups[1].Value;
        parameters = match.Groups[2].Value.Split(',');
      }
    }
    if (!phrases.ContainsKey(name)) {
      phrases.Add(name, new Phrase());
    }
    phrases[name].versions.Add(new Version(language,
                                           parameters,
                                           optional,
                                           ParseLingoona(value),
                                           location));
  }

  public IEnumerable<ClassifiedTextRun> ParseLingoona(string value) {
    string last_token = null;
    foreach (string token in Regex.Split(value, @"(<<|>>)")) {
      if (token == "<<") {
        yield return new ClassifiedTextRun(
            PredefinedClassificationTypeNames.Operator,
            token);
      } else if (token == ">>") {
        yield return new ClassifiedTextRun(
            PredefinedClassificationTypeNames.Operator,
            token);
      } else if (last_token == "<<") {
        string placeholder = token;
        if (placeholder.Contains(':')) {
          string[] grammar_placeholder = token.Split(new[] { ':' }, 2);
          string grammar = grammar_placeholder[0];
          placeholder = grammar_placeholder[1];
          // TODO(egg): Parse grammar, which may be comma-separated.
          yield return new ClassifiedTextRun(
              PredefinedClassificationTypeNames.Keyword,
              grammar);
          yield return new ClassifiedTextRun(
              PredefinedClassificationTypeNames.Operator,
              ":");
        }
        if (Regex.IsMatch(placeholder, @"^\d+$")) {
          yield return new ClassifiedTextRun(
              PredefinedClassificationTypeNames.Number,
              placeholder);
        } else {
          foreach (var run in ParseLingoonaString(placeholder)) {
            yield return run;
          }
        }
      } else {
        foreach (var run in ParseLingoonaString(token)) {
          yield return run;
        }
      }
      last_token = token;
    }
  }

  public IEnumerable<ClassifiedTextRun> ParseLingoonaString(string value) {
    int i = 0;
    foreach (string part in Regex.Split(value, @"(\^[PpFfMmNn][^ ]*)")) {
      if (i++ % 2 == 0) {
        yield return new ClassifiedTextRun(
            PredefinedClassificationTypeNames.String,
            part);
      } else {
        yield return new ClassifiedTextRun(
            PredefinedClassificationTypeNames.Operator,
            "^");
        yield return new ClassifiedTextRun(
            PredefinedClassificationTypeNames.Keyword,
            part.Substring(1));
      }
    }
  }

  public readonly Dictionary<string, Phrase> phrases =
      new Dictionary<string, Phrase>();
  public readonly HashSet<string> files_ = new HashSet<string>();
  DirectoryInfo deepest_common_ancestor_ = null;
  private FileSystemWatcher watcher_ = null;
}

}
