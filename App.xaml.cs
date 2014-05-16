using System;
using System.Windows;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for App.xaml
   /// </summary>
   public partial class App : Application {
      protected override void OnStartup(StartupEventArgs e) {
         base.OnStartup(e);
         var rom = Utils.LoadRom(e.Args);
         if (rom == null) { this.Shutdown(); return; }
         Func<byte[], IElementFactory> create = data => {
            IElementFactory factory = new DataHolder(data);
            factory = new GbaHeaderFormatter(factory, data);
            factory = GbaLzFormatterFactory.Images(factory, data);
            factory = GbaLzFormatterFactory.Palette(factory, data);
            factory = new GbaPointerFormatter(factory, data);
            factory = new RangeChecker(factory);
            return factory;
         };
         var window = new MainWindow(create, rom);
         window.Show();
      }
   }
}
