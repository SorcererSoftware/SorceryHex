using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SorceryHex.Gba {
   namespace Pokemon {
      [Export(typeof(IModelFactory))]
      public class Factory : IModelFactory {
         public string DisplayName { get { return "Pokemon Gba Game"; } }

         public bool CanCreateModel(string name, byte[] data) {
            if (!name.ToLower().EndsWith("gba")) return false;
            var code = Header.GetCode(data);

            //           ruby            sapphire          emerald
            if (code == "AXVE" || code == "AXPE" || code == "BPEE") return true;

            //          firered          leafgreen
            if (code == "BPRE" || code == "BPGE") return true;

            return false;
         }

         public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
            var pointerMapper = new PointerMapper(data);
            var pcs = new PCS();
            // var imageguess = new ImageGuess(pointerMapper);
            var storage = new RunStorage(data
               , new Header(pointerMapper)
               , new Thumbnails(pointerMapper)
               , new Lz(pointerMapper)
               , new Pokemon.DataTypes.ScriptedDataTypes(pointerMapper, pcs, scriptInfo.Engine, scriptInfo.Scope)
               , pcs
               // , imageguess
            );
            // new Window { Content = new ScrollViewer { Content = imageguess.Panel } }.Show();
            IModel model = new CompositeModel(data, storage);
            model = new PointerParser(model, data, storage, pointerMapper);
            return model;
         }

         public int CompareTo(IModelFactory other) {
            if (other is StandardFactory) return 1;
            if (other is DefaultFactory) return 1;
            if (other is SimpleFactory) return 1;
            return 0;
         }
      }
   }

   [Export(typeof(IModelFactory))]
   public class StandardFactory : IModelFactory {
      public string DisplayName { get { return "Gba Game"; } }

      public bool CanCreateModel(string name, byte[] data) {
         return name.ToLower().EndsWith("gba");
      }

      public IModel CreateModel(string name, byte[] data, ScriptInfo scriptInfo) {
         var pointerMapper = new Gba.PointerMapper(data);
         var storage = new RunStorage(data
            , new Gba.Header(pointerMapper)
            , new Gba.Lz(pointerMapper)
         );
         IModel model = new CompositeModel(data, storage);
         model = new Gba.PointerParser(model, data, storage, pointerMapper);
         return model;
      }

      public int CompareTo(IModelFactory other) {
         if (other is Pokemon.Factory) return -1;
         if (other is DefaultFactory) return 1;
         if (other is SimpleFactory) return 1;
         return 0;
      }
   }

   class ImageGuess : IRunParser {
      const int WidthHeight = 0x20, ByteCount = WidthHeight * WidthHeight / 2;
      readonly SimpleDataRun _guessRun;
      readonly PointerMapper _pointers;
      public readonly Panel Panel = new WrapPanel();
      public ImageGuess(PointerMapper pointers) {
         _pointers = pointers;
         _guessRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, Solarized.Brushes.Cyan), ByteCount) {
            Interpret = (data, location) => {
               var dataBytes = new byte[ByteCount];
               Array.Copy(data, location, dataBytes, 0, dataBytes.Length);
               var source = ImageUtils.Expand16bitImage(dataBytes, ImageUtils.DefaultPalette, WidthHeight, WidthHeight);
               return new Image { Source = source, Width = WidthHeight * 1.5, Height = WidthHeight * 1.5 };
            }
         };
      }
      public IEnumerable<int> Find(string term) { yield break; }
      public void Load(ICommandFactory commander, IRunStorage runs) {
         _pointers.ClaimDeferred(runs);
         var destinations = _pointers.OpenDestinations.OrderBy(i => i).ToArray();
         var list = new List<int>();
         for (int i = 0; i < destinations.Length; i++) {
            var loc = destinations[i];
            if (runs.Data[loc] != 0x00) continue;
            if (i < destinations.Length - 1) {
               if (destinations[i + 1] < loc + ByteCount) continue;
               // if ((destinations[i + 1] - loc) % ByteCount != 0) continue;
            }
            // _pointers.Claim(runs, _guessRun, loc);
            list.Add(loc);
         }
         Panel.Dispatcher.Invoke(() => {
            foreach (var item in list) Panel.Children.Add(new ImageGuessResult(runs.Data, item, 2, 2));
         });
      }
   }

   class ImageGuessResult : Image {
      readonly byte[] _data;
      readonly int _location;
      int width;
      int height;
      public ImageGuessResult(byte[] data, int location, int initialX, int initialY) {
         _data = data; _location = location; width = initialX; height = initialY;
         ToolTip = Utils.ToHexString(_location);
         BuildSource();
         Margin = new Thickness(2, 2, 2, 2);
      }

      Point p;
      protected override void OnMouseDown(MouseButtonEventArgs e) {
         p = e.GetPosition(this);
         e.Handled = true;
         CaptureMouse();
         base.OnMouseDown(e);
      }

      protected override void OnMouseMove(MouseEventArgs e) {
         if (!IsMouseCaptured) {
            base.OnMouseMove(e); return;
         }
         var p2 = e.GetPosition(this);
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
         var dataBytes = new byte[width * height * 0x80];
         Array.Copy(_data, _location, dataBytes, 0, dataBytes.Length);
         var source = ImageUtils.Expand16bitImage(dataBytes, ImageUtils.DefaultPalette, width * 0x10, height * 0x10);
         Source = source;
         Width = width * 0x18;
         Height = height * 0x18;
      }
   }
}
