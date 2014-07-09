using System.Windows;

namespace SorceryHex.Gba {
   /// <summary>
   /// Interaction logic for ImageSearchWindow.xaml
   /// </summary>
   public partial class ImageSearchWindow : Window {
      public ImageSearchWindow(ImageGuess guesser) {
         InitializeComponent();
         imageList.ItemsSource = guesser.Items;
      }
   }
}
