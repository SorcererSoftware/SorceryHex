using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SorceryHex {
   public class CursorController {
      readonly MainWindow _window;
      readonly MainCommandFactory _commandFactory;
      readonly Queue<FrameworkElement> _interpretationBackgrounds = new Queue<FrameworkElement>();

      int _selectionStart, _selectionLength = 1;
      int _clickCount;
      Point _mouseDownPosition;

      public CursorController(MainWindow window, MainCommandFactory commandFactory) {
         _window = window;
         _commandFactory = commandFactory;
         _window.ResizeGrid.MouseLeftButtonDown += BodyMouseDown;
         _window.ResizeGrid.MouseRightButtonDown += BodyRightMouseDown;
         _window.ResizeGrid.MouseMove += BodyMouseMove;
         _window.ResizeGrid.MouseLeftButtonUp += BodyMouseUp;
         _window.JumpCompleted += HandleJumpCompleted;

         _window.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, ExecuteCopy, CanExecuteCopy));
      }

      public void UpdateSelection(IModel model, int location) {
         _selectionStart = model.GetDataBlockStart(location);
         _window.CurrentTab.CursorStart = _selectionStart;
         _selectionLength = model.GetDataBlockLength(location);
         _window.CurrentTab.CursorLocation = _window.CurrentTab.CursorStart + _selectionLength - 1;
         UpdateSelection();
      }

      public void UpdateSelection() {
         ClearBackground();
         UpdateSelectionStartFromCursorLocation();

         for (int i = 0; i < _selectionLength; i++) {
            int loc = _selectionStart + i - _window.CurrentTab.Offset;
            if (loc < 0) continue;
            if (loc >= _window.CurrentTab.Columns * _window.CurrentTab.Rows) continue;
            var rectangle = _interpretationBackgrounds.Count > 0 ? _interpretationBackgrounds.Dequeue() : new Border {
               Background = Solarized.Theme.Instance.Backlight
            };
            rectangle.SetCreator(this);
            MainWindow.SplitLocation(rectangle, _window.CurrentTab.Columns, loc);
            _window.BackgroundBody.Children.Add(rectangle);
         }
      }

      public void Recycle(FrameworkElement item) { _interpretationBackgrounds.Enqueue(item); }

      public bool HandleKey(Key key) {
         if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return false;
         int add = 1;
         switch (key) {
            case Key.Left:
            case Key.Up:
            case Key.PageUp:
            case Key.Home:
               if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selectionLength > 1) {
                  _window.CurrentTab.CursorLocation = _selectionStart;
               } else {
                  if (key == Key.Up) add = _window.CurrentTab.Columns;
                  if (key == Key.PageUp) add = _window.CurrentTab.Columns * _window.CurrentTab.Columns;
                  if (key == Key.Home) add = (_window.CurrentTab.CursorLocation - _window.CurrentTab.Offset) % _window.CurrentTab.Columns;
                  _window.CurrentTab.CursorLocation -= add;
               }
               break;
            case Key.Right:
            case Key.Down:
            case Key.PageDown:
            case Key.End:
               if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selectionLength > 1) {
                  _window.CurrentTab.CursorLocation = _selectionStart + _selectionLength - 1;
               } else {
                  if (key == Key.Down) add = _window.CurrentTab.Columns;
                  if (key == Key.PageDown) add = _window.CurrentTab.Columns * _window.CurrentTab.Rows;
                  if (key == Key.End) add = _window.CurrentTab.Columns - (_window.CurrentTab.CursorLocation - _window.CurrentTab.Offset) % _window.CurrentTab.Columns - 1;
                  _window.CurrentTab.CursorLocation += add;
               }
               break;
            default:
               if (_selectionLength > 1) return false;
               var c = Utils.Convert(key);
               if (c == null) return false;
               int editLocation = _window.CurrentTab.CursorLocation;
               _window.CurrentTab.Model.Edit(_window.CurrentTab.Model.Segment.Inner(editLocation), (char)c);
               _window.RefreshElement(editLocation);
               return true;
         }

         if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _window.CurrentTab.CursorStart = _window.CurrentTab.CursorLocation;
         UpdateSelectionStartFromCursorLocation();
         UpdateSelectionFromMovement();
         return true;
      }

      #region Events

      void BodyMouseDown(object sender, MouseButtonEventArgs e) {
         _window.EditBody.Children.Clear();
         _window.MainFocus();
         _mouseDownPosition = e.GetPosition(_window.Body);
         if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _window.CurrentTab.CursorStart = ByteOffsetForMouse(e);
         _window.Body.CaptureMouse();
         _clickCount = e.ClickCount;
      }

      void BodyRightMouseDown(object sender, MouseButtonEventArgs e) {
         var position = ByteOffsetForMouse(e);
         if (_selectionStart <= position && position < _selectionStart + _selectionLength) {
            _window.BodyContextMenu.Items.Clear();
            var item = new MenuItem { Header = "Duplicate" };
            item.Click += (sender1, e1) => {
               _window.Duplicate(_selectionStart, _selectionLength);
            };
            _window.BodyContextMenu.Items.Add(item);
         }
      }

      void BodyMouseMove(object sender, MouseEventArgs e) {
         if (!_window.Body.IsMouseCaptured) return;
         if (e.LeftButton != MouseButtonState.Pressed) return;

         _window.CurrentTab.CursorLocation = ByteOffsetForMouse(e);
         UpdateSelection();
      }

      void BodyMouseUp(object sender, MouseButtonEventArgs e) {
         if (!_window.Body.IsMouseCaptured) return;
         _window.Body.ReleaseMouseCapture();
         if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || _clickCount > 1) {
            var upPosition = e.GetPosition(_window.Body);
            var dif = _mouseDownPosition - upPosition;
            dif = new Vector(Math.Abs(dif.X), Math.Abs(dif.Y));
            if (dif.X <= SystemParameters.MinimumHorizontalDragDistance && dif.Y <= SystemParameters.MinimumVerticalDragDistance) {
               _commandFactory.CheckJumpForMouseOver();
            }
            return;
         }
         if (_selectionLength != 1) return;
         var editor = _window.CurrentTab.Model.CreateElementEditor(_window.CurrentTab.Model.Segment.Inner(_window.CurrentTab.CursorLocation));
         if (editor == null) return;
         Grid.SetColumnSpan(editor, 3);
         int loc = _window.CurrentTab.CursorLocation - _window.CurrentTab.Offset;
         MainWindow.SplitLocation(editor, _window.CurrentTab.Columns, loc);
         int currentColumn = Grid.GetColumn(editor);
         if (currentColumn > 0) Grid.SetColumn(editor, currentColumn - 1);
         if (currentColumn > _window.CurrentTab.Columns - 3) Grid.SetColumn(editor, _window.CurrentTab.Columns - 3);
         _window.EditBody.Children.Add(editor);
      }

      public void HandleMoveNext(object sender, UpdateLocationEventArgs e) {
         if (_window.EditBody.Children.Count > 0 || e.UpdateList.Count() > 0) {
            _window.EditBody.Children.Clear();
            e.UpdateList.Foreach(_window.RefreshElement);
            return;
         }
         _window.CurrentTab.CursorLocation++;
         UpdateSelectionFromMovement();
      }

      public void HandleJumpCompleted(object sender, EventArgs e) {
         // don't move the cursor after a jump - leave it where it was instead
      }

      #endregion

      #region Commands

      void CanExecuteCopy(object sender, CanExecuteRoutedEventArgs e) {
         e.CanExecute = _selectionLength > 0;
      }

      void ExecuteCopy(object sender, RoutedEventArgs e) {
         var portion = new byte[_selectionLength];
         Array.Copy(_window.Data, _window.CurrentTab.CursorLocation, portion, 0, _selectionLength);
         var hex = portion.Select(p => Utils.ToHexString(p, 2)).Aggregate((a, b) => a + " " + b);
         Clipboard.SetText(hex);
      }

      #endregion

      #region Helpers

      void UpdateSelectionStartFromCursorLocation() {
         if (_window.CurrentTab.CursorLocation > _window.CurrentTab.CursorStart) {
            _selectionStart = _window.CurrentTab.CursorStart;
            _selectionLength = _window.CurrentTab.CursorLocation - _window.CurrentTab.CursorStart + 1;
         } else {
            _selectionStart = _window.CurrentTab.CursorLocation;
            _selectionLength = _window.CurrentTab.CursorStart - _window.CurrentTab.CursorLocation + 1;
         }
      }

      void ClearBackground() {
         var children = new List<FrameworkElement>();
         foreach (FrameworkElement child in _window.BackgroundBody.Children) children.Add(child);
         foreach (var child in children.Where(c => c.GetCreator() == this)) {
            _window.BackgroundBody.Children.Remove(child);
            _interpretationBackgrounds.Enqueue(child);
         }
      }

      int ByteOffsetForMouse(MouseEventArgs e) {
         var position = e.GetPosition(_window.Body);
         int row = (int)(position.Y / MainWindow.ElementHeight);
         int col = (int)(position.X / MainWindow.ElementWidth);
         return row * _window.CurrentTab.Columns + col + _window.CurrentTab.Offset;
      }

      void UpdateSelectionFromMovement() {
         if (_window.CurrentTab.CursorLocation < _window.CurrentTab.Offset) {
            int rowCount = (int)Math.Ceiling((double)(_window.CurrentTab.Offset - _window.CurrentTab.CursorLocation) / _window.CurrentTab.Columns);
            if (rowCount > _window.CurrentTab.Columns) {
               _window.JumpTo(_window.CurrentTab.Offset - rowCount * _window.CurrentTab.Columns);
            } else {
               _window.ShiftRows(rowCount);
            }
         } else if (_window.CurrentTab.CursorLocation >= _window.CurrentTab.Offset + _window.CurrentTab.Columns * _window.CurrentTab.Rows) {
            int rowCount = (int)Math.Floor((double)(_window.CurrentTab.CursorLocation - (_window.CurrentTab.Offset + _window.CurrentTab.Rows * _window.CurrentTab.Columns)) / _window.CurrentTab.Columns + 1);
            if (rowCount > _window.CurrentTab.Rows) {
               _window.JumpTo(_window.CurrentTab.Offset + rowCount * _window.CurrentTab.Columns);
            } else {
               _window.ShiftRows(-rowCount);
            }
         }

         _window.UpdateHeaderText();
      }

      #endregion
   }
}
