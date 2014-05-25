using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SorceryHex {
   public interface ICommandFactory {
      void CreateJumpCommand(FrameworkElement element, params int[] jumpLocation);
      void RemoveJumpCommand(FrameworkElement element);
      void LinkToInterpretation(FrameworkElement element, FrameworkElement visual);
      void UnlinkFromInterpretation(FrameworkElement element);
   }

   class MainCommandFactory : ICommandFactory {
      readonly MainWindow _window;
      readonly Dictionary<FrameworkElement, FrameworkElement> _interpretations = new Dictionary<FrameworkElement, FrameworkElement>();
      readonly Dictionary<FrameworkElement, int> _interpretationReferenceCounts = new Dictionary<FrameworkElement, int>();
      readonly Dictionary<FrameworkElement, int[]> _jumpers = new Dictionary<FrameworkElement, int[]>();
      readonly Queue<FrameworkElement> _interpretationBackgrounds = new Queue<FrameworkElement>();

      bool _sortInterpretations;

      public MainCommandFactory(MainWindow window) { _window = window; }

      #region Command Factory

      #region Jump

      public void CreateJumpCommand(FrameworkElement element, params int[] jumpLocation) {
         _jumpers[element] = jumpLocation;
      }

      public void RemoveJumpCommand(FrameworkElement element) {
         _jumpers.Remove(element);
      }

      #endregion

      #region Interpretation

      public void LinkToInterpretation(FrameworkElement element, FrameworkElement visual) {
         _interpretations[element] = visual;
         visual.Margin = new Thickness(5);
         if (!_interpretationReferenceCounts.ContainsKey(visual)) _interpretationReferenceCounts[visual] = 0;
         _interpretationReferenceCounts[visual]++;
         if (_interpretationReferenceCounts[visual] == 1) {
            _sortInterpretations = true;
            visual.MouseEnter += MouseEnterInterpretation;
            visual.MouseLeave += MouseLeaveInterpretation;
         }
      }

      public void UnlinkFromInterpretation(FrameworkElement element) {
         var visual = _interpretations[element];
         _interpretations.Remove(element);
         _interpretationReferenceCounts[visual]--;
         if (_interpretationReferenceCounts[visual] == 0) {
            _window.InterpretationPane.Children.Remove(visual);
            _interpretationReferenceCounts.Remove(visual);
            visual.MouseEnter -= MouseEnterInterpretation;
            visual.MouseLeave -= MouseLeaveInterpretation;
         }
      }

      #endregion

      #endregion

      public void CheckJumpForMouseOver() {
         var element = _jumpers.Keys.FirstOrDefault(jumper => jumper.IsMouseOver);
         if (element != null) {
            var list = _jumpers[element];
            if (list.Length > 1) {
               _window.BodyContextMenu.Items.Clear();
               foreach (var dest in list) {
                  var header = dest.ToHexString();
                  while (header.Length < 6) header = "0" + header;
                  var item = new MenuItem { Header = header };
                  _window.BodyContextMenu.Items.Add(item);
                  item.Click += (s, e1) => {
                     _window.AddLocationToBreadCrumb();
                     _window.JumpTo(item.Header.ToString().ParseAsHex());
                  };
               }
               _window.BodyContextMenu.IsOpen = true;
            } else {
               _window.AddLocationToBreadCrumb();
               _window.JumpTo(list[0]);
            }
         }
      }

      public void SortInterpretations() {
         if (_sortInterpretations) {
            var interpretations = _interpretations.Values.Distinct().OrderBy(KeyElementLocation);
            _window.InterpretationPane.Children.Clear();
            foreach (var element in interpretations) _window.InterpretationPane.Children.Add(element);
            _sortInterpretations = false;
         }
      }

      public void Recycle(FrameworkElement element) {
         _interpretationBackgrounds.Enqueue(element);
      }

      #region Interpretation Helpers

      void MouseEnterInterpretation(object sender, EventArgs e) {
         var visual = (FrameworkElement)sender;

         foreach (var element in _interpretations.Keys.Where(key => _interpretations[key] == visual).Select(FindElementInBody)) {
            var rectangle = _interpretationBackgrounds.Count > 0 ? _interpretationBackgrounds.Dequeue() : new Border {
               Background = Solarized.Theme.Instance.Backlight
            };
            rectangle.SetCreator(this);
            int loc = MainWindow.CombineLocation(element, _window.CurrentColumnCount);
            MainWindow.SplitLocation(rectangle, _window.CurrentColumnCount, loc);
            _window.BackgroundBody.Children.Add(rectangle);
         }
      }

      void MouseLeaveInterpretation(object sender, EventArgs e) {
         var children = new List<FrameworkElement>();
         foreach (FrameworkElement child in _window.BackgroundBody.Children) children.Add(child);
         foreach (var child in children.Where(c => c.GetCreator() == this)) {
            _window.BackgroundBody.Children.Remove(child);
            _interpretationBackgrounds.Enqueue(child);
         }
      }

      #endregion

      int KeyElementLocation(FrameworkElement interpretation) {
         var keysForInterpretation = _interpretations.Keys.Where(key => _interpretations[key] == interpretation).ToList();
         Debug.Assert(keysForInterpretation.Count() == _interpretationReferenceCounts[interpretation]);
         // wrapped elements are not directly in the body and don't have a row/column.
         return keysForInterpretation.Select(FindElementInBody).Select(key => MainWindow.CombineLocation(key, _window.CurrentColumnCount)).Min();
      }

      FrameworkElement FindElementInBody(FrameworkElement element) {
         while (!_window.Body.Children.Contains(element)) element = (FrameworkElement)element.Parent;
         return element;
      }
   }
}
