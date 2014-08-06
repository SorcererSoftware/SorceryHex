using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SorceryHex {
   class AddLineDecorator : IModel {
      readonly IModel _model;
      readonly ButtonBase _addButton = new Button { Content = "+", HorizontalContentAlignment = HorizontalAlignment.Center };

      public AddLineDecorator(ICommandFactory commander, IModel model, int lineLength) {
         _model = model;
         _model.MoveToNext += (sender, e) => MoveToNext(sender, e);
         _addButton.Click += (sender, e) => {
            _model.Append(commander, lineLength);
            MoveToNext(this, new UpdateLocationEventArgs(Enumerable.Range(_model.Segment.Length - lineLength, lineLength + 1).ToArray()));
         };
      }

      #region Boilerplate
      public IModel Duplicate(int start, int length) { return _model.Duplicate(start, length); }
      public ISegment Segment { get { return _model.Segment; } }
      public void Load(ICommandFactory commander) { _model.Load(commander); }
      public void Recycle(ICommandFactory commander, FrameworkElement element) { if (element != _addButton) _model.Recycle(commander, element); }
      public bool IsStartOfDataBlock(int location) { return _model.IsStartOfDataBlock(location); }
      public bool IsWithinDataBlock(int location) { return _model.IsWithinDataBlock(location); }
      public string GetLabel(int location) { return _model.GetLabel(location); }
      public int GetDataBlockStart(int location) { return _model.GetDataBlockStart(location); }
      public int GetDataBlockLength(int location) { return _model.GetDataBlockLength(location); }
      public FrameworkElement GetInterpretation(int location) { return _model.GetInterpretation(location); }
      public IEnumerable<int> Find(string term) { return _model.Find(term); }
      public FrameworkElement CreateElementEditor(ISegment segment) { return _model.CreateElementEditor(segment); }
      public void Append(ICommandFactory commander, int length) { _model.Append(commander, length); }
      public void Edit(ISegment segment, char c) { _model.Edit(segment, c); }
      public void CompleteEdit(ISegment segment) { _model.CompleteEdit(segment); }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;
      #endregion

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var elements = _model.CreateElements(commander, start, length);
         if (start <= _model.Segment.Length && _model.Segment.Length < start + length) elements[_model.Segment.Length - start] = _addButton;
         return elements;
      }
   }

   /// <summary>
   /// Interaction logic for DuplicateTab.xaml
   /// </summary>
   partial class DuplicateTab : IDataTab {

      public readonly int OriginalOffset;
      public readonly int OriginalLength;
      readonly IDataTabContainer _container;

      public IModel Model { get; private set; }
      public int Offset { get; set; }
      public int Columns { get; private set; }
      public int Rows { get; private set; }
      public int CursorStart { get; set; }
      public int CursorLocation { get; set; }
      public bool IsHomeTab { get { return false; } }

      public DuplicateTab(IDataTabContainer container, ICommandFactory commander, IModel model, int columns, int rows, int duplicateFrom) {
         _container = container;
         Model = new AddLineDecorator(commander, model, columns);
         Columns = columns;
         Rows = rows;
         OriginalOffset = duplicateFrom;
         Offset = CursorStart = CursorLocation = 0;

         InitializeComponent();

         this.Content = new TextBlock {
            Foreground = Solarized.Brushes.Blue,
            TextDecorations = TextDecorations.Underline,
            Text = OriginalOffset.ToHexString(6),
            HorizontalAlignment = HorizontalAlignment.Center
         };
      }

      public bool Resize(int columns, int rows) {
         Rows = rows;
         return true;
      }

      protected override void OnMouseDown(MouseButtonEventArgs e) {
         base.OnMouseDown(e);
         if (e.LeftButton == MouseButtonState.Pressed) {
            _container.SelectTab(this);
            e.Handled = true;
         } else if (e.MiddleButton == MouseButtonState.Pressed) {
            e.Handled = true;
            _container.RemoveTab(this);
         }
      }

      #region Events

      void ReplaceOriginal(object sender, EventArgs e) {
         if (Model.Segment.Length <= OriginalLength) {
            _container.PushData(this, OriginalOffset);
            return;
         }
         var offset = _container.FindFreeSpace(Model.Segment.Length);
         _container.PushData(this, offset);
         _container.Repoint(OriginalOffset, offset);
      }

      #endregion
   }
}
