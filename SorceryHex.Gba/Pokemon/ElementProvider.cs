using SorceryHex.Gba.Pokemon.DataTypes;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex.Gba.Pokemon {
   class SpeciesElementProvider : IElementProvider {
      public static readonly IElementProvider Instance = new SpeciesElementProvider();
      SpeciesElementProvider() { }

      readonly Queue<Image> _recycles = new Queue<Image>();
      readonly Queue<Rectangle> _empties = new Queue<Rectangle>();
      readonly IDictionary<int, ImageSource> _cache = new Dictionary<int, ImageSource>();

      public bool IsEquivalent(IElementProvider other) { return other == this; }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         if (innerIndex == 1) return ProvideEmpty();
         int index = data.ReadShort(runStart);
         var image = ProvideImage();
         ImageSource source;
         if (_cache.ContainsKey(index)) {
            source = _cache[index];
         } else {
            source = Thumbnails.CropIcon(Thumbnails.GetIcon(data, data.ReadShort(runStart)));
            source.Freeze();
            _cache[index] = source;
         }
         image.Source = source;
         return image;
      }

      public string ProvideString(byte[] data, int runStart, int runLength) {
         // TODO provide the pokemon name
         return data.ReadData(runLength, runStart).ToHexString();
      }

      public void Recycle(FrameworkElement element) {
         if (element is Image) _recycles.Enqueue((Image)element);
         else if (element is Rectangle) _empties.Enqueue((Rectangle)element);
         else Debug.Fail("Cannot deal with this kind of element.");
      }

      FrameworkElement ProvideEmpty() {
         if (_empties.Count > 0) return _empties.Dequeue();
         return new Rectangle();
      }

      Image ProvideImage() {
         if (_recycles.Count > 0) return _recycles.Dequeue();
         var image = new Image { Width = MainWindow.ElementWidth, Height = MainWindow.ElementHeight };
         Grid.SetColumnSpan(image, 2);
         return image;
      }
   }

   class JumpElementProvider : IElementProvider {
      readonly GeometryElementProvider _provider = new GeometryElementProvider(Utils.ByteFlyweights, Solarized.Brushes.Orange, true);
      readonly ChildJump _jump;
      public JumpElementProvider(ChildJump jump) { _jump = jump; }

      public bool IsEquivalent(IElementProvider other) {
         var that = other as JumpElementProvider;
         if (that == null) return false;
         return _jump == that._jump;
      }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         var element = _provider.ProvideElement(commandFactory, data, runStart, innerIndex, runLength);
         var reader = new Reader(data, runStart);
         int jump = _jump(reader);
         commandFactory.CreateJumpCommand(element, jump);
         return element;
      }

      public string ProvideString(byte[] data, int runStart, int runLength) { return null; }

      public void Recycle(FrameworkElement element) { _provider.Recycle(element); }
   }
}
