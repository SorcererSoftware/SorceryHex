using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for DataTab.xaml
   /// </summary>
   public partial class DataTab : IDataTab {
      private readonly IDataTabContainer _container;
      private readonly bool _isHomeTab;

      public readonly int OriginalOffset;
      public readonly bool ColumnsFixed = false; // TODO

      public IModel Model { get; private set; }
      public int Columns { get; private set; }
      public int Rows { get; private set; }
      public int Offset { get; set; }
      public int CursorStart { get; set; }
      public int CursorLocation { get; set; }
      public bool IsHomeTab { get { return _isHomeTab; } }

      public DataTab(IDataTabContainer container, IModel model, int columns, int rows, int offset, bool isHomeTab = false) {
         _container = container;
         Model = model;
         Columns = columns;
         Rows = rows;
         OriginalOffset = Offset = CursorStart = CursorLocation = offset;
         _isHomeTab = isHomeTab;

         InitializeComponent();
         this.Content = new TextBlock {
            Foreground = Solarized.Theme.Instance.Emphasis,
            Text = OriginalOffset.ToHexString(6),
            HorizontalAlignment = HorizontalAlignment.Center
         };
      }

      public bool Resize(int columns, int rows) {
         if (ColumnsFixed) return false;
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
            e.Handled = true;
            _container.RemoveTab(this);
         }
         // currently no rightclick options
      }
   }
}
