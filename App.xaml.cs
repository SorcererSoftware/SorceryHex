using System;
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
            IElementFactory factory = new DataHolder(data);
            factory = new Gba.HeaderFormatter(factory, data);
            factory = new Gba.PCS(factory, data);
            factory = Gba.LzFormatterFactory.Images(factory, data);
            factory = Gba.LzFormatterFactory.Palette(factory, data);
            factory = new Gba.PointerFormatter(factory, data);
            factory = new RangeChecker(factory);
            return factory;
         };
         var window = new MainWindow(create, fileName, rom);
         window.Show();
      }
   }
}
