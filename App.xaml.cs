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
         Func<string, byte[], IParser> create = (name, data) => {
            // TODO move gba/pokemon stuff to a separate assembly
            if (!name.EndsWith(".gba")) {
               return new CompositeParser(data);
            }

            var storage = new RunStorage(data);
            var pointers = data.FindPossiblePointers();
            storage.AddLzImage(pointers);
            IParser factory = new CompositeParser(data
               , storage
               // Gba.LzFactory.Palette(data),
               // Gba.LzFactory.Images(data),
               // new Gba.PCS(data),
               // new Gba.Maps(data),
               // new Gba.Header(data)
               );
            return factory;
            // return new Gba.PointerFormatter(factory, data);
         };
         var window = new MainWindow(create, fileName, contents);
         window.Show();
      }
   }
}
