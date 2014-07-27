using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SorceryHex.Gba {
   namespace Pokemon {
      [Export(typeof(IModelFactory))]
      public class Factory : IModelFactory {
         public string DisplayName { get { return "Pokemon Gba Game"; } }

         public string Version { get { return "1.0"; } }

         public bool CanCreateModel(string name, byte[] data) {
            if (!name.ToLower().EndsWith("gba")) return false;
            var code = Header.GetCode(new GbaSegment(data, 0));

            //           ruby            sapphire          emerald
            if (code == "AXVE" || code == "AXPE" || code == "BPEE") return true;

            //          firered          leafgreen
            if (code == "BPRE" || code == "BPGE") return true;

            return false;
         }

         public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
            var pointerMapper = new PointerMapper(data);
            var pcs = new PCS();
            // TODO fix this
            // var imageguess = new ImageGuess(pointerMapper, new Rectangle().Dispatcher);
            var defaultSegment = new GbaSegment(data, 0, data.Length);
            var storage = new RunStorage(defaultSegment
               , new Header(pointerMapper)
               , new Thumbnails(pointerMapper)
               , new Lz(pointerMapper)
               , new Pokemon.DataTypes.ScriptedDataTypes(pointerMapper, pcs, scriptInfo)
               , pcs
               // , imageguess
            );
            // new ImageSearchWindow(imageguess).Show();
            IModel model = new CompositeModel(defaultSegment, storage);
            model = new PointerParser(model, data, storage, pointerMapper);
            return model;
         }

         public int CompareTo(IModelFactory other) {
            if (other is StandardFactory) return 1;
            if (other is DefaultFactory) return 1;
            if (other is SimpleFactory) return 1;
            if (other is StringFactory) return 1;
            return 0;
         }
      }
   }

   [Export(typeof(IModelFactory))]
   public class StandardFactory : IModelFactory {
      public string DisplayName { get { return "Gba Game"; } }

      public string Version { get { return "1.0"; } }

      public bool CanCreateModel(string name, byte[] data) {
         return name.ToLower().EndsWith("gba");
      }

      public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
         var pointerMapper = new Gba.PointerMapper(data);
         var storage = new RunStorage(new GbaSegment(data, 0)
            , new Gba.Header(pointerMapper)
            , new Gba.Lz(pointerMapper)
         );
         IModel model = new CompositeModel(new GbaSegment(data, 0, data.Length), storage);
         model = new Gba.PointerParser(model, data, storage, pointerMapper);
         return model;
      }

      public int CompareTo(IModelFactory other) {
         if (other is Pokemon.Factory) return -1;
         if (other is DefaultFactory) return 1;
         if (other is SimpleFactory) return 1;
         if (other is StringFactory) return 1;
         return 0;
      }
   }

   public class ImageGuess : IRunParser {
      const int WidthHeight = 0x20, ByteCount = WidthHeight * WidthHeight / 2;
      readonly SimpleDataRun _guessRun;
      readonly PointerMapper _pointers;
      readonly Dispatcher _dispatcher;
      public readonly IList<ImageGuessResult> Items = new List<ImageGuessResult>();
      public ImageGuess(PointerMapper pointers, Dispatcher dispatcher) {
         _pointers = pointers;
         _dispatcher = dispatcher;
         _guessRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, Solarized.Brushes.Cyan), ByteCount) {
            Interpret = segment => {
               var source = ImageUtils.Expand16bitImage(segment, ImageUtils.DefaultPalette, WidthHeight, WidthHeight);
               return new Image { Source = source, Width = WidthHeight * 1.5, Height = WidthHeight * 1.5 };
            }
         };
      }
      public IEnumerable<int> Find(string term) { yield break; }
      public void Load(ICommandFactory commander, IRunStorage runs) {
         _pointers.ClaimDeferred(commander, runs);
         var destinations = _pointers.OpenDestinations.OrderBy(i => i).ToArray();
         var list = new List<int>();
         for (int i = 0; i < destinations.Length; i++) {
            var loc = destinations[i];
            if (runs.Segment[loc] != 0x00) continue;

            var noise = ImageUtils.ImageNoise(runs.Segment.Inner(loc), 32, 32);
            if (noise > 1) continue;

            // TODO reject if the noise level is higher than 1.
            if (i < destinations.Length - 1) {
               if (destinations[i + 1] < loc + ByteCount) continue;
               // if ((destinations[i + 1] - loc) % ByteCount != 0) continue;
            }
            // _pointers.Claim(runs, _guessRun, loc);
            list.Add(loc);
         }
         _dispatcher.Invoke((Action)(() => {
            // foreach (var item in list) Items.Add(new ImageGuessResult(runs.Data, item, 2, 2));
         }));
      }
   }

   public class ImageGuessResult : Image {
      readonly ISegment _segment;
      int width;
      int height;
      public ImageGuessResult(ISegment segment, int initialX, int initialY) {
         _segment = segment;
         width = initialX; height = initialY;
         ToolTip = Utils.ToHexString(segment.Location);
         BuildSource();
         Margin = new Thickness(2, 2, 2, 2);
      }

      Point p;
      protected override void OnMouseDown(MouseButtonEventArgs e) {
         p = e.GetPosition((FrameworkElement)this.VisualParent);
         e.Handled = true;
         CaptureMouse();
         base.OnMouseDown(e);
      }

      protected override void OnMouseMove(MouseEventArgs e) {
         if (!IsMouseCaptured) {
            base.OnMouseMove(e); return;
         }
         var p2 = e.GetPosition(((FrameworkElement)this.VisualParent));
         var dist = (p2 - p);
         var changed = false;
         while (dist.X > 10) {
            dist.X -= 10;
            width++;
            changed = true;
         }
         while (dist.X < -10) {
            dist.X += 10;
            width--; if (width < 1) width = 1;
            changed = true;
         }
         while (dist.Y > 10) {
            dist.Y -= 10;
            height++;
            changed = true;
         }
         while (dist.Y < -10) {
            dist.Y += 10;
            height--; if (height < 1) height = 1;
            changed = true;
         }
         if (changed) {
            BuildSource();
            ReleaseMouseCapture();
            p = p2;
            CaptureMouse();
         }
         e.Handled = true;
         base.OnMouseMove(e);
      }

      protected override void OnMouseUp(MouseButtonEventArgs e) {
         if (IsMouseCaptured) {
            ReleaseMouseCapture();
            e.Handled = true;
         }
         base.OnMouseUp(e);
      }

      void BuildSource() {
         var source = ImageUtils.Expand16bitImage(_segment, ImageUtils.DefaultPalette, width * 0x10, height * 0x10);
         var noise = ImageUtils.ImageNoise(_segment, width * 0x10, height * 0x10);
         Source = source;
         Width = width * 0x18;
         Height = height * 0x18;
         ToolTip = Utils.ToHexString(_segment.Location) + " : " + noise;
      }
   }
}
