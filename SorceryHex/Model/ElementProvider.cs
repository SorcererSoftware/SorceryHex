using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IElementProvider {
      FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength);
      bool IsEquivalent(IElementProvider other);
      void Recycle(FrameworkElement element);
   }

   public class GeometryElementProvider : IElementProvider {
      readonly Queue<Border> _recycles = new Queue<Border>();
      readonly Geometry[] _parser;
      readonly Brush _runColor;
      readonly bool _underline;
      readonly string _hoverText;

      public GeometryElementProvider(Geometry[] parser, Brush runColor, bool underline = false, string hoverText = null) {
         _parser = parser;
         _runColor = runColor;
         _underline = underline;
         _hoverText = hoverText;
      }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         var geometry = _parser[data[runStart + innerIndex]];
         if (geometry == null) return null;

         var element = _recycles.Count > 0 ? _recycles.Dequeue() : new Border {
            Child = new Path {
               HorizontalAlignment = HorizontalAlignment.Center,
               VerticalAlignment = VerticalAlignment.Center,
               Margin = new Thickness(3, 3, 3, 1),
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1)
         };
         ((Path)element.Child).Data = geometry;
         ((Path)element.Child).Fill = _runColor;
         element.BorderBrush = _runColor;

         double leftBorder = innerIndex == 0 ? 2 : 0;
         double rightBorder = innerIndex - 1 == runLength ? 2 : 0;
         element.Margin = new Thickness(leftBorder, 0, rightBorder, 1);
         double bottom = _underline ? 1 : 0;
         element.BorderThickness = new Thickness(0, 0, 0, bottom);
         element.ToolTip = _hoverText;

         return element;
      }

      public bool IsEquivalent(IElementProvider other) {
         var that = other as GeometryElementProvider;
         if (that == null) return false;
         if (this._parser != that._parser) return false;
         if (this._runColor != that._runColor) return false;
         if (this._underline != that._underline) return false;
         if (this._hoverText != that._hoverText) return false;
         return true;
      }

      public void Recycle(FrameworkElement element) { _recycles.Enqueue((Border)element); }
   }

   public class EnumElementProvider : IElementProvider {
      readonly Queue<Rectangle> _rectangles = new Queue<Rectangle>();
      readonly Queue<TextBlock> _textblocks = new Queue<TextBlock>();
      readonly dynamic[] _names;
      readonly int _stride;
      readonly Brush _runColor;
      readonly string _hoverText;

      public EnumElementProvider(dynamic[] names, int stride, Brush runColor, string hoverText = null) {
         _names = names;
         _stride = stride;
         _runColor = runColor;
         _hoverText = string.IsNullOrEmpty(hoverText) ? null : hoverText;
      }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         if (innerIndex != 0) {
            var rectangle = _rectangles.Count > 0 ? _rectangles.Dequeue() : new Rectangle();
            return rectangle;
         }

         var block = _textblocks.Count > 0 ? _textblocks.Dequeue() : new TextBlock {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.None,
            FontSize = 10,
            Margin = new Thickness(2, 0, 2, 0),
            Foreground = _runColor
         };
         var index = data.ReadData(_stride, runStart);
         string name = "???";
         if (index < _names.Length) {
            if (_names[index].ToString() == "{ name }") name = _names[index].name;
            else name = _names[index].ToString();
         }
         block.Text = name;
         block.ToolTip = (_hoverText ?? string.Empty) + " : " + name;

         Grid.SetColumnSpan(block, _stride);
         return block;
      }

      public bool IsEquivalent(IElementProvider other) {
         var that = other as EnumElementProvider;
         if (that == null) return false;
         if (this._names != that._names) return false;
         if (this._runColor != that._runColor) return false;
         if (this._hoverText != that._hoverText) return false;
         return true;
      }

      public void Recycle(FrameworkElement element) {
         if (element is TextBlock) _textblocks.Enqueue((TextBlock)element);
         else if (element is Rectangle) _rectangles.Enqueue((Rectangle)element);
         else Debug.Fail("EnumElementProvider cannot recycle " + element);
      }
   }
}
