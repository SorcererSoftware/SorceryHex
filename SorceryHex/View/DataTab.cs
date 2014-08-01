using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {

   public interface IDataTabContainer {
      void SelectTab(DataTab tab);
      void RemoveTab(DataTab tab);
   }

   /// <summary>
   /// Controls scrollbar.
   /// Controls Headers.
   /// Watches panel width/height.
   /// </summary>
   public class DataTab : UserControl {
      public readonly IDataTabContainer Container;
      public readonly IModel Model;
      public readonly bool IsHomeTab;
      public int Offset { get; set; }
      public int Columns { get; set; }
      public int Rows { get; set; }
      public int CursorStart { get; set; }
      public int CursorLocation { get; set; }

      public DataTab(IDataTabContainer container, IModel model, int offset, int columns, int rows, bool isHomeTab = false) {
         Container = container;
         Model = model;
         Offset = offset;
         Columns = columns;
         Rows = rows;
         IsHomeTab = isHomeTab;
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

      public void Resize(int columns, int rows) {
         Columns = columns;
         Rows = rows;
      }

      protected override void OnMouseDown(MouseButtonEventArgs e) {
         base.OnMouseDown(e);
         if (e.LeftButton == MouseButtonState.Pressed) {
            Container.SelectTab(this);
            e.Handled = true;
         } else if (e.MiddleButton == MouseButtonState.Pressed && !IsHomeTab) {
            Container.RemoveTab(this);
            e.Handled = true;
         }
         // currently no rightclick options
      }
   }
}
