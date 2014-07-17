types.WaitFor "_setup"
types.WaitFor "0poketype"

_attackdata = 0x1CC
_attackdata = 0xCA54 if types.Version == "AXVE" || types.Version == "AXPE"

self.attackdata = types.ReadArray attackcount, _attackdata, ->(b) {
   b.Byte "effect"
   b.Byte "power"
   b.ByteEnum "type", poketype
   b.Byte "accuracy"
   b.Byte "pp"
   b.Byte "unknown1"
   b.Short "target"
   b.Word "unknown2"
}

types.Label attackdata, ->(i) { return attackname[i].name }
types.AddShortcut "attackdata", attackdata[0].Location