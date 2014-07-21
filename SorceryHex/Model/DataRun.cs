using System.Windows;

namespace SorceryHex {
   public delegate int[] JumpRule(byte[] data, int index);
   public delegate FrameworkElement InterpretationRule(ISegment segment);

   public interface IDataRun {
      InterpretationRule Interpret { get; }
      JumpRule Jump { get; }
      IEditor Editor { get; }
      IElementProvider Provider { get; }

      ISegment GetLength(ISegment segment);
   }

   public class SimpleDataRun : IDataRun {
      static readonly IEditor DefaultEditor = new DisableEditor();
      readonly int _length;

      public InterpretationRule Interpret { get; set; }
      public JumpRule Jump { get; set; }
      public IEditor Editor { get; set; }
      public IElementProvider Provider { get; private set; }

      public SimpleDataRun(IElementProvider provider, int length, IEditor editor = null) {
         Provider = provider;
         _length = length;
         Editor = editor ?? DefaultEditor;
      }

      public ISegment GetLength(ISegment segment) { return segment.Resize(_length); }
   }
}
