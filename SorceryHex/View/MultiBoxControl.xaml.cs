using IronRuby;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SorceryHex {
   public interface IAppCommands {
      int Offset { get; }
      byte[] Data { get; }
      void MainFocus();
      void JumpTo(int location, bool addToBreadCrumbs = false);
      void JumpTo(string label, bool addToBreadCrumbs = false);
      IEnumerable<int> Find(string term);
      void WriteStatus(string status);
   }

   public class ScriptCommands {
      readonly MultiBoxControl _box;
      readonly IAppCommands _app;
      readonly ScriptEngine _engine;
      readonly ScriptScope _scope;
      public ScriptCommands(MultiBoxControl control, IAppCommands commands, ScriptEngine engine, ScriptScope scope) {
         _box = control;
         _app = commands;
         _engine = engine;
         _scope = scope;
      }
      public int offset { get { return _app.Offset; } }
      public int[] find(string term) { return _app.Find(term).ToArray(); }
      public byte[] data { get { return _app.Data; } }
      public void @goto(int offset) { _app.JumpTo(offset, true); }
      public void @goto(string label) { _app.JumpTo(label, true); }
      public string[] performance() { return AutoTimer.Report.ToArray(); }
      public IEnumerable<string> vars() { return _scope.GetVariableNames(); }
      public void status(string status) { _app.WriteStatus(status); }
      public void run(string filename) {
         if (!filename.Contains(".")) filename += ".rb";
         var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
         foreach (var subdir in dir.EnumerateDirectories().Concat(new[] { dir })) {
            var file = subdir.ToString() + Path.DirectorySeparatorChar + filename;
            if (!File.Exists(file)) continue;
            var source = _engine.CreateScriptSourceFromFile(file);
            Task.Factory.StartNew(() => {
               _app.WriteStatus("Executing " + file);
               source.Execute(_scope);
               _app.WriteStatus("Done");
            });
            return;
         }
         throw new FileNotFoundException("Couldn't find " + filename);
      }
      public IEnumerable<string> help() {
         yield return "app.help - show this document.";
         yield return "app.data - the raw bytes of the loaded file. You may edit them like so: app.data[2] = 0x10";
         yield return "app.vars - show a list of available varibales.";
         yield return "app.find 'searchstring' - find a specific piece of data or text in the file and show the locations in a list.";
         yield return "app.run 'filename' - look in subfolders to find filename.rb and execute it.";
         yield return "app.goto <location> - jump to a specific location in the data. Location can be a number in decimal or hex, or a variable in quotes.";
         yield return "app.offset - list the current app location in hex.";
         yield return "app.status 'text' - update the status line underneath the data to 'text'";
         yield return "app.performance - list performance metrics. Useful for debugging / finding slow scripts";
      }
   }

   public class ScriptInfo {
      public readonly ScriptEngine Engine;
      public readonly ScriptScope Scope;
      public readonly Action<IList<string>> ShowScriptErrors;
      public ScriptInfo(Action<IList<string>> errorFunc) {
         Engine = Ruby.CreateEngine();
         Scope = Engine.CreateScope();
         ShowScriptErrors = errorFunc;
      }
   }

   /// <summary>
   /// Interaction logic for MultiBoxControl.xaml
   /// </summary>
   public partial class MultiBoxControl : UserControl {
      readonly IAppCommands _appCommands;
      readonly Popup _popup;
      readonly TextBlock _outputText;

      IList<int> _findPositions;
      int _findIndex;

      public readonly ScriptInfo ScriptInfo;

      public MultiBoxControl(IAppCommands appCommands) {
         InitializeComponent();
         _appCommands = appCommands;

         _outputText = new TextBlock {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Background = Solarized.Theme.Instance.Backlight
         };
         _popup = new Popup {
            Child = _outputText,
            MinHeight = MainWindow.ElementHeight,
            Placement = PlacementMode.Bottom,
            PlacementTarget = ScriptBox,
            StaysOpen = false
         };

         ScriptInfo = new ScriptInfo(ShowScriptStartupErrors);
         SetupScope();
      }

      #region Helpers

      public void Show(dynamic result) {
         if (result == null) {
            _outputText.Text = string.Empty;
            _popup.IsOpen = false;
            return;
         }
         _outputText.Foreground = Solarized.Theme.Instance.Emphasis;
         _outputText.Text = Parse(result);
         _popup.IsOpen = !string.IsNullOrEmpty(_outputText.Text);
      }

      public void AddLocationToBreadCrumb() {
         if (BreadCrumbBar.Children.Count >= 5) {
            ((Button)BreadCrumbBar.Children[0]).Click -= BackExecuted;
            BreadCrumbBar.Children.RemoveAt(0);
         }
         var hex = _appCommands.Offset.ToHexString();
         while (hex.Length < 6) hex = "0" + hex;
         var button = new Button { Content = hex };
         button.Click += BackExecuted;
         BreadCrumbBar.Children.Add(button);
      }

      public void ResetScope() {
         ScriptInfo.Scope.GetVariableNames().Foreach(name => ScriptInfo.Scope.RemoveVariable(name));
         _scopeNeedsSetup = true;
         SetupScope();
      }

      void UpdateVisibility(FrameworkElement visibleControl) {
         foreach (var control in new FrameworkElement[] { BreadCrumbBar, MultiBoxContainer, ScriptContainer }) {
            control.Visibility = control == visibleControl ? Visibility.Visible : Visibility.Collapsed;
         }

         if (visibleControl == BreadCrumbBar) {
            _appCommands.MainFocus();
         } else if (visibleControl == MultiBoxContainer) {
            Keyboard.Focus(MultiBoxInput);
            MultiBoxInput.SelectAll();
         } else if (visibleControl == ScriptContainer) {
            Keyboard.Focus(ScriptBox);
            ScriptBox.SelectAll();
         } else {
            Debug.Fail("UpdateVisibility might need to be update.");
         }
      }

      bool _scopeNeedsSetup = true;
      void SetupScope() {
         if (!_scopeNeedsSetup) return;
         _scopeNeedsSetup = false;
         ScriptInfo.Scope.SetVariable("app", new ScriptCommands(this, _appCommands, ScriptInfo.Engine, ScriptInfo.Scope));
      }

      void ShowScriptStartupErrors(IList<string> errors) {
         Dispatcher.Invoke((Action)(() => {
            _outputText.Foreground = Solarized.Brushes.Red;
            Debug.Assert(errors.Count > 0);
            if (errors.Count == 1) {
               _outputText.Text = "The following error occured when running startup scripts:" + Environment.NewLine;
               _outputText.Text += errors[0];
            } else {
               _outputText.Text = "The following errors occured when running startup scripts:" + Environment.NewLine;
               _outputText.Text += errors.Aggregate((a, b) => a + Environment.NewLine + b);
            }
            _popup.IsOpen = true;
         }));
      }

      #endregion

      #region Event Handlers

      void HandleMultiBoxKey(object sender, KeyEventArgs e) {
         if (e.Key == Key.Escape) {
            UpdateVisibility(BreadCrumbBar);
            return;
         }

         if (MultiBoxLabel.Text == "Goto") {
            HandleGotoKey(e);
         } else if (MultiBoxLabel.Text == "Find") {
            HandleFindKey(e);
         }
      }

      void HandleGotoKey(KeyEventArgs e) {
         // sanitize for goto // TODO move to textchanged
         int caret = MultiBoxInput.CaretIndex;
         int selection = MultiBoxInput.SelectionLength;
         MultiBoxInput.Text = new string(MultiBoxInput.Text.ToUpper().Where(Utils.Hex.Contains).ToArray());
         MultiBoxInput.CaretIndex = Math.Min(caret, MultiBoxInput.Text.Length);
         MultiBoxInput.SelectionLength = selection;

         // check for special keys
         if (e.Key == Key.Enter) {
            int hex = MultiBoxInput.Text.ParseAsHex();
            _appCommands.JumpTo(hex, true);
            UpdateVisibility(BreadCrumbBar);
         }

         // only allow hex keys
         if (!Utils.HexKeys.Contains(e.Key)) e.Handled = true;
      }

      void HandleFindKey(KeyEventArgs e) {
         // dumb find: make it smarter later

         // check for special keys
         if (e.Key == Key.Enter) {
            _appCommands.WriteStatus("Searching");
            var searchEnumerable = _appCommands.Find(MultiBoxInput.Text);
            Task task;
            _findPositions = searchEnumerable.BackgroundEnumerate(out task);
            if (task != null) {
               Action complete = () => _appCommands.WriteStatus("Search Complete: " + _findPositions.Count + " results.");
               task.ContinueWith(t => Dispatcher.Invoke(complete));
               task.Start();
            }
            if (_findPositions.Count == 0) {
               MessageBox.Show("No matches found for: " + MultiBoxInput.Text);
               return;
            }
            _findIndex = 0;
            _appCommands.JumpTo(_findPositions[_findIndex]);
            UpdateVisibility(BreadCrumbBar);
         }
      }

      void HandleScriptKey(object sender, KeyEventArgs e) {
         if (e.Key == Key.Escape) {
            if (_popup.IsOpen) {
               _popup.IsOpen = false;
               return;
            }
            UpdateVisibility(BreadCrumbBar);
            return;
         }

         if (e.Key == Key.Enter) {
            try {
               var result = ScriptInfo.Engine.CreateScriptSourceFromString(ScriptBox.Text, SourceCodeKind.SingleStatement).Execute(ScriptInfo.Scope);
               Show(result);
            } catch (Exception e1) {
               _outputText.Foreground = Solarized.Brushes.Red;
               _outputText.Text = "error: " + e1.Message;
               _popup.IsOpen = true;
            }
         }
      }

      public static string Parse(object result) {
         string output = "";
         if (result is string) {
            output += result + Environment.NewLine;
         } else if (result is IEnumerable) {
            foreach (var element in (IEnumerable)result) {
               output += Parse(element) + Environment.NewLine;
            }
            return output;
         }

         output = result.ToString();
         int num; if (int.TryParse(output, out num)) {
            output = "0x" + num.ToHexString();
         }
         return output;
      }

      void CloseClick(object sender, EventArgs e) { UpdateVisibility(BreadCrumbBar); }

      #endregion

      #region Commands

      void ScriptExecuted(object sender, EventArgs e) {
         MultiBoxLabel.Text = "Script";
         UpdateVisibility(ScriptContainer);
      }

      void FindExecuted(object sender, EventArgs e) {
         MultiBoxLabel.Text = "Find";
         UpdateVisibility(MultiBoxContainer);
      }

      void FindPreviousExecuted(object sender, EventArgs e) {
         if (_findPositions == null || _findPositions.Count == 0) return;
         _findIndex--;
         if (_findIndex < 0) _findIndex = _findPositions.Count - 1;
         _appCommands.JumpTo(_findPositions[_findIndex]);
      }

      void FindNextExecuted(object sender, EventArgs e) {
         if (_findPositions == null || _findPositions.Count == 0) return;
         _findIndex++;
         if (_findIndex >= _findPositions.Count) _findIndex = 0;
         _appCommands.JumpTo(_findPositions[_findIndex]);
      }

      void GotoExecuted(object sender, EventArgs e) {
         MultiBoxLabel.Text = "Goto";
         UpdateVisibility(MultiBoxContainer);
      }

      void BackExecuted(object sender, EventArgs e) {
         if (sender == null || sender is MenuItem || sender is Window) {
            sender = BreadCrumbBar.Children[BreadCrumbBar.Children.Count - 1];
         }
         var button = sender as Button;
         Debug.Assert(BreadCrumbBar.Children.Contains(button));
         int address = button.Content.ToString().ParseAsHex();
         _appCommands.JumpTo(address);
         var index = BreadCrumbBar.Children.IndexOf(button);
         while (BreadCrumbBar.Children.Count > index) {
            ((Button)BreadCrumbBar.Children[index]).Click -= BackExecuted;
            BreadCrumbBar.Children.RemoveAt(index);
         }
      }

      void BackCanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = BreadCrumbBar.Children.Count > 0; }

      void FindNavigationCanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = _findPositions != null && _findPositions.Count > 0; }

      void Always(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = true; }

      #endregion
   }
}
