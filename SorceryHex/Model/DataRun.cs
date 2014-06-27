using System.Windows;

namespace SorceryHex {
   public delegate int[] JumpRule(byte[] data, int index);
   public delegate FrameworkElement InterpretationRule(byte[] data, int index);

   public interface IDataRun {
      InterpretationRule Interpret { get; }
      JumpRule Jump { get; }
      IEditor Editor { get; }
      IElementProvider Provider { get; }

      int GetLength(byte[] data, int startPoint);
   }

   public class SimpleDataRun : IDataRun {
      static readonly IEditor DefaultEditor = new DisableEditor();
      readonly int _length;

      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }
      public IEditor Editor { get; set; }
      public IElementProvider Provider { get; private set; }

      public SimpleDataRun(IElementProvider provider, int length) {
         Provider = provider;
         _length = length;
         Editor = DefaultEditor;
      }

      public int GetLength(byte[] data, int startPoint) { return _length; }
   }
}
