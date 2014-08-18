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
      public readonly IModel Model;
      readonly ButtonBase _addButton = new Button { Content = "+", HorizontalContentAlignment = HorizontalAlignment.Center };

      public AddLineDecorator(ICommandFactory commander, IModel model, int lineLength) {
         Model = model;
         Model.MoveToNext += (sender, e) => MoveToNext(sender, e);
         _addButton.Click += (sender, e) => {
            Model.Append(commander, lineLength);
            MoveToNext(this, new UpdateLocationEventArgs(Enumerable.Range(Model.Segment.Length - lineLength, lineLength + 1).ToArray()));
         };
      }

      #region Boilerplate
      public ISegment Segment { get { return Model.Segment; } }
      public void Append(ICommandFactory commander, int length) { Model.Append(commander, length); }
      public int Repoint(int initialLocation, int newLocation) { return Model.Repoint(initialLocation, newLocation); }
      public IModel Duplicate(int start, int length) { return Model.Duplicate(start, length); }
      public void Replace(int originalOffset, int originalLength, IModel model, int newOffset) { Model.Replace(originalOffset, originalLength, model, newOffset); }
      public int FindFreeSpace(int length) { return Model.FindFreeSpace(length); }
      public void Load(ICommandFactory commander) { Model.Load(commander); }
      public void Recycle(ICommandFactory commander, FrameworkElement element) { if (element != _addButton) Model.Recycle(commander, element); }
      public bool IsStartOfDataBlock(int location) { return Model.IsStartOfDataBlock(location); }
      public bool IsWithinDataBlock(int location) { return Model.IsWithinDataBlock(location); }
      public string GetLabel(int location) { return Model.GetLabel(location); }
      public int GetDataBlockStart(int location) { return Model.GetDataBlockStart(location); }
      public int GetDataBlockLength(int location) { return Model.GetDataBlockLength(location); }
      public FrameworkElement GetInterpretation(int location) { return Model.GetInterpretation(location); }
      public IEnumerable<int> Find(string term) { return Model.Find(term); }
      public FrameworkElement CreateElementEditor(ISegment segment) { return Model.CreateElementEditor(segment); }
      public void Edit(ISegment segment, char c) { Model.Edit(segment, c); }
      public void CompleteEdit(ISegment segment) { Model.CompleteEdit(segment); }
      public event EventHandler<UpdateLocationEventArgs> MoveToNext;
      #endregion

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var elements = Model.CreateElements(commander, start, length);
         if (start <= Model.Segment.Length && Model.Segment.Length < start + length) elements[Model.Segment.Length - start] = _addButton;
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
         OriginalLength = model.Segment.Length;
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

      // TODO feature envy: this code should be pushed to the _container
      void ReplaceOriginal(object sender, EventArgs e) {
         if (Model.Segment.Length <= OriginalLength) {
            _container.PushData(this, OriginalOffset);
            return;
         }
         var offset = _container.FindFreeSpace(Model.Segment.Length);
         _container.PushData(this, offset);
         _container.Repoint(OriginalOffset, offset); // this gets mad about the original pointer not existing
         // TODO delete original data
      }

      #endregion
   }
}
