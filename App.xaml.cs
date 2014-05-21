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
         var contents = Utils.LoadFile(out fileName, e.Args);
         if (contents == null) { this.Shutdown(); return; }
         Func<string, byte[], IElementFactory> create = (name, data) => {
            // TODO move gba/pokemon stuff to a separate assembly
            if (!name.EndsWith(".gba")) {
               return new CompositeElementFactory(data);
            }

            IElementFactory factory = new CompositeElementFactory(data,
               Gba.LzFactory.Palette(data),
               Gba.LzFactory.Images(data),
               new Gba.PCS(data),
               new Gba.Header(data));
            return new Gba.PointerFormatter(factory, data);
         };
         var window = new MainWindow(create, fileName, contents);
         window.Show();
      }
   }
}
