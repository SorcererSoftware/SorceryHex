layout = types.FindVariableArray "p", ->(b) {
   b.AttackListPointer
}

types.AddShortcut "learnmoves", layout.destination
self.learnmoves = layout.data