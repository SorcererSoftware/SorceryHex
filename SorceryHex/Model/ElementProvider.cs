using System.Collections.Generic;
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

}
