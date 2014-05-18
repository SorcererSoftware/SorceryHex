using System;
using System.Collections.Generic;
using System.Windows;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for App.xaml
   /// </summary>
   public partial class App : Application {
      protected override void OnStartup(StartupEventArgs e) {
         base.OnStartup(e);
         string fileName;
         var rom = Utils.LoadRom(out fileName, e.Args);
         if (rom == null) { this.Shutdown(); return; }
         Func<byte[], IElementFactory> create = data => {
            IElementFactory factory = new CompositeElementFactory(data,
               Gba.LzFactory.Palette(data),
               Gba.LzFactory.Images(data),
               new Gba.PCS(data),
               new Gba.Header(data));
            return new Gba.PointerFormatter(factory, data);
         };
         var window = new MainWindow(create, fileName, rom);
         window.Show();
      }
   }
}
