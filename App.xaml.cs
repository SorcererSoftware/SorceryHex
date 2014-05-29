﻿using System;
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

            var pointerMapper = new Gba.PointerMapper(data);
            var storage = new RunStorage(data, new Gba.Header(pointerMapper), new Gba.Lz(pointerMapper), new Gba.Maps(pointerMapper), new Gba.PCS());
            // TODO Gba.Maps
            IParser factory = new CompositeParser(data, storage);
            factory = new Gba.PointerParser(factory, data, storage, pointerMapper);
            return factory;
         };
         var window = new MainWindow(create, fileName, contents);
         window.Show();
      }
   }
}
