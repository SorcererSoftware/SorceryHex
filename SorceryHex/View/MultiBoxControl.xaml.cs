using IronPython.Hosting;
using IronRuby;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SorceryHex {
   public interface IAppCommands {
      int Offset { get; }
      byte[] Data { get; }
      void MainFocus();
      void JumpTo(int location, bool addToBreadCrumbs = false);
      int[] Find(string term);
   }

   public class ScriptCommands {
      readonly IAppCommands app;
      public ScriptCommands(IAppCommands commands) { app = commands; }
      public int offset { get { return app.Offset; } }
      public int[] find(string term) { return app.Find(term); }
      public byte[] data { get { return app.Data; } }
      public void @goto(int offset) { app.JumpTo(offset, true); }
      public string[] performance() { return AutoTimer.Report.ToArray(); }
   }

   public class ScriptInfo { public ScriptEngine Engine; public ScriptScope Scope; }

   /// <summary>
   /// Interaction logic for MultiBoxControl.xaml
   /// </summary>
   public partial class MultiBoxControl : UserControl {
      readonly IAppCommands _appCommands;
      readonly ScriptEngine _engine = Ruby.CreateEngine();
      readonly ScriptScope _scope;
      readonly Popup _popup;
      readonly TextBlock _outputText;

      IList<int> _findPositions;
      int _findIndex;

      public ScriptInfo ScriptInfo { get { return new ScriptInfo { Engine = _engine, Scope = _scope }; } }

      public MultiBoxControl(IAppCommands appCommands) {
         InitializeComponent();
         _appCommands = appCommands;
         _scope = _engine.CreateScope();

         _outputText = new TextBlock { Background = Solarized.Theme.Instance.Backlight };
         _popup = new Popup {
            Child = _outputText,
            MinHeight = MainWindow.ElementHeight,
            Placement = PlacementMode.Bottom,
            PlacementTarget = ScriptBox,
            StaysOpen = false
         };
      }

      #region Helpers

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
         _scope.SetVariable("app", new ScriptCommands(_appCommands));
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
            _findPositions = _appCommands.Find(MultiBoxInput.Text);
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
               SetupScope();
               var result = _engine.CreateScriptSourceFromString(ScriptBox.Text, SourceCodeKind.SingleStatement).Execute(_scope);
               if (result == null) {
                  _popup.IsOpen = false;
                  return;
               }
               _outputText.Foreground = Solarized.Theme.Instance.Primary;
               _outputText.Text = Parse(result);
            } catch (Exception e1) {
               _outputText.Foreground = Solarized.Brushes.Red;
               _outputText.Text = "error: " + e1.Message;
            }
            _popup.IsOpen = true;
         }
      }

      string Parse(dynamic result) {
         string output = "";
         if (result is string) {
            output += result + Environment.NewLine;
         } else if (result is IEnumerable) {
            foreach (var element in result) {
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
