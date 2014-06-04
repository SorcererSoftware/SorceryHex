﻿using System;
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

      int _selectionStart, _selectionLength;
      int _clickStart;
      Point _mouseDownPosition;

      public CursorController(MainWindow window, MainCommandFactory commandFactory) {
         _window = window;
         _commandFactory = commandFactory;
         _window.ResizeGrid.MouseLeftButtonDown += BodyMouseDown;
         _window.ResizeGrid.MouseMove += BodyMouseMove;
         _window.ResizeGrid.MouseLeftButtonUp += BodyMouseUp;
      }

      public void UpdateSelection(IModel model, int location) {
         _selectionStart = model.GetDataBlockStart(location);
         _selectionLength = model.GetDataBlockLength(location);
         UpdateSelection();
      }

      public void UpdateSelection() {
         ClearBackground();

         for (int i = 0; i < _selectionLength; i++) {
            int loc = _selectionStart + i - _window.Offset;
            if (loc < 0) continue;
            if (loc >= _window.CurrentColumnCount * _window.CurrentRowCount) continue;
            var rectangle = _interpretationBackgrounds.Count > 0 ? _interpretationBackgrounds.Dequeue() : new Border {
               Background = Solarized.Theme.Instance.Backlight
            };
            rectangle.SetCreator(this);
            MainWindow.SplitLocation(rectangle, _window.CurrentColumnCount, loc);
            _window.BackgroundBody.Children.Add(rectangle);
         }
      }

      public void Recycle(FrameworkElement item) { _interpretationBackgrounds.Enqueue(item); }

      public void HandleKey(Key key) {
         switch (key) {
            case Key.Left:
            case Key.Up:
               if (_selectionLength > 1) {
                  _selectionLength = 1;
               } else {
                  _selectionStart -= (key == Key.Up) ? _window.CurrentColumnCount : 1;
               }
               UpdateSelectionFromMovement();
               break;
            case Key.Right:
            case Key.Down:
               if (_selectionLength > 1) {
                  _selectionStart += _selectionLength - 1; _selectionLength = 1;
               } else {
                  _selectionStart += (key == Key.Down) ? _window.CurrentColumnCount : 1;
               }
               UpdateSelectionFromMovement();
               break;
            default:
               if (_selectionLength > 1) return;
               var c = Utils.Convert(key);
               if (c == null) return;
               int editLocation = _selectionStart;
               _window.Holder.Edit(editLocation, (char)c);
               _window.RefreshElement(editLocation);
               break;
         }
      }

      #region Events

      void BodyMouseDown(object sender, MouseButtonEventArgs e) {
         _window.MainFocus();
         _mouseDownPosition = e.GetPosition(_window.Body);
         _clickStart = ByteOffsetForMouse(e);
         _window.Body.CaptureMouse();
      }

      void BodyMouseMove(object sender, MouseEventArgs e) {
         if (!_window.Body.IsMouseCaptured) return;
         if (e.LeftButton != MouseButtonState.Pressed) return;

         int currentLoc = ByteOffsetForMouse(e);
         if (currentLoc > _clickStart) {
            _selectionStart = _clickStart;
            _selectionLength = currentLoc - _clickStart + 1;
         } else {
            _selectionStart = currentLoc;
            _selectionLength = _clickStart - currentLoc + 1;
         }
         UpdateSelection();
      }

      void BodyMouseUp(object sender, MouseButtonEventArgs e) {
         _window.Body.ReleaseMouseCapture();
         if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
            var upPosition = e.GetPosition(_window.Body);
            var dif = (_mouseDownPosition - upPosition);
            dif = new Vector(Math.Abs(dif.X), Math.Abs(dif.Y));
            if (dif.X <= SystemParameters.MinimumHorizontalDragDistance && dif.Y <= SystemParameters.MinimumVerticalDragDistance) {
               _commandFactory.CheckJumpForMouseOver();
            }
         }
      }

      public void HandleMoveNext(object sender, EventArgs e) {
         _selectionStart++;
         UpdateSelectionFromMovement();
      }

      #endregion

      #region Helpers

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
         return row * _window.CurrentColumnCount + col + _window.Offset;
      }

      void UpdateSelectionFromMovement() {
         if (_selectionStart < _window.Offset) {
            int rowCount = (int)Math.Ceiling((double)(_window.Offset - _selectionStart) / _window.CurrentColumnCount);
            if (rowCount > _window.CurrentRowCount) {
               _window.JumpTo(_window.Offset - rowCount * _window.CurrentColumnCount);
            } else {
               _window.ShiftRows(rowCount);
            }
         } else if (_selectionStart >= _window.Offset + _window.CurrentColumnCount * _window.CurrentRowCount) {
            int rowCount = (int)Math.Floor((double)(_selectionStart - (_window.Offset + _window.CurrentRowCount * _window.CurrentColumnCount)) / _window.CurrentColumnCount + 1);
            if (rowCount > _window.CurrentRowCount) {
               _window.JumpTo(_window.Offset + rowCount * _window.CurrentColumnCount);
            } else {
               _window.ShiftRows(-rowCount);
            }
         }

         _window.UpdateHeaderText();
      }

      #endregion
   }
}