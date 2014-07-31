using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   /// <summary>
   /// Controls scrollbar.
   /// Controls Headers.
   /// Watches panel width/height.
   /// </summary>
   public class DataTab : UserControl {
      public readonly IModel Model;
      public int Offset { get; set; }
      public int Columns { get; set; }
      public int Rows { get; set; }
      public int CursorStart { get; set; }
      public int CursorLocation { get; set; }

      public DataTab(IModel model, int offset, int columns, int rows) {
         Model = model;
         Offset = offset;
         Columns = columns;
         Rows = rows;
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
   }
}
