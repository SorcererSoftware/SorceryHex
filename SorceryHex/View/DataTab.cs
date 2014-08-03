using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IDataTabContainer {
      void SelectTab(IDataTab tab);
      void RemoveTab(IDataTab tab);

      void PushData(DuplicateTab duplicateTab, int _originalOffset);
      int FindFreeSpace(int length);
      void Repoint(int originalOffset, int newOffset);
   }

   /// <summary>
   /// Controls scrollbar.
   /// Controls Headers.
   /// Watches panel width/height.
   /// </summary>
   public interface IDataTab {
      IModel Model { get; }
      int Offset { get; set; }
      int Columns { get; }
      int Rows { get; }
      int CursorStart { get; set; }
      int CursorLocation { get; set; }
      bool IsHomeTab { get; }

      bool Resize(int columns, int rows);
   }

   public class DataTab1 : UserControl, IDataTab {
      private readonly IDataTabContainer _container;
      private readonly bool _isHomeTab;

      public IModel Model { get; private set; }
      public int Offset { get; set; }
      public int Columns { get; private set; }
      public int Rows { get; private set; }
      public int CursorStart { get; set; }
      public int CursorLocation { get; set; }
      public bool IsHomeTab { get { return _isHomeTab; } }

      public DataTab1(IDataTabContainer container, IModel model, int offset, int columns, int rows, bool isHomeTab = false) {
         _container = container;
         Model = model;
         Offset = offset;
         Columns = columns;
         Rows = rows;
         _isHomeTab = isHomeTab;
         CursorStart = CursorLocation = 0;

         Width = 60; Height = 18;

         // TODO make visual elsewhere - possibly xaml
         // TODO consider other possible classes. Use TabItems? Radio Buttons?
         var grid = new Grid(); Content = grid;
         grid.Children.Add(new Path {
            Data = new CombinedGeometry {
               GeometryCombineMode = GeometryCombineMode.Exclude,
               Geometry1 = new RectangleGeometry { Rect = new System.Windows.Rect(0, 0, 10, 10), RadiusX = 1, RadiusY = 2 },
               Geometry2 = new RectangleGeometry { Rect = new System.Windows.Rect(0, 5, 10, 5) }
            },
            Stroke = Solarized.Theme.Instance.Secondary,
            Fill = Solarized.Theme.Instance.Backlight,
            Stretch = Stretch.Fill
         });
         grid.Children.Add(new TextBlock {
            Foreground = Solarized.Theme.Instance.Emphasis,
            Text = offset.ToHexString(6),
            HorizontalAlignment = HorizontalAlignment.Center
         });
      }

      public bool Resize(int columns, int rows) {
         Columns = columns;
         Rows = rows;
         return true;
      }

      protected override void OnMouseDown(MouseButtonEventArgs e) {
         base.OnMouseDown(e);
         if (e.LeftButton == MouseButtonState.Pressed) {
            _container.SelectTab(this);
            e.Handled = true;
         } else if (e.MiddleButton == MouseButtonState.Pressed && !IsHomeTab) {
            _container.RemoveTab(this);
            e.Handled = true;
         }
         // currently no rightclick options
      }
   }
}
