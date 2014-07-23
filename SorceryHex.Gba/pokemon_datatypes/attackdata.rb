types.WaitFor "_setup"
types.WaitFor "0poketype"

_attackdata = 0x1CC
_attackdata = 0xCA54 if types.Version == "AXVE" || types.Version == "AXPE"

_targetlist = [
   "single opponent", #0
   "dependent", # for moves like mirror move / metronome
   "--",
   "--",
   "random opponent", #4
   "--",
   "--",
   "--",
   "both opponents", #8
   "--",
   "--",
   "--",
   "--", #C
   "--",
   "--",
   "--",
   "self status", #10
   "--",
   "--",
   "--",
   "--", #14
   "--",
   "--",
   "--",
   "--", #18
   "--",
   "--",
   "--",
   "--", #1C
   "--",
   "--",
   "--",
   "all", #20
   "--",
   "--",
   "--",
   "--", #24
   "--",
   "--",
   "--",
   "--", #28
   "--",
   "--",
   "--",
   "--", #2C
   "--",
   "--",
   "--",
   "--", #30
   "--",
   "--",
   "--",
   "--", #34
   "--",
   "--",
   "--",
   "--", #38
   "--",
   "--",
   "--",
   "--", #3C
   "--",
   "--",
   "--",
   "hazard", #40
]

self.attackdata = types.ReadArray attackcount, _attackdata, ->(b) {
   b.Byte "effect"
   b.ByteNum "power"
   b.ByteEnum "type", poketype
   b.ByteNum "accuracy"
   b.ByteNum "pp"
   b.Byte "unknown1"
   b.ByteEnum "target", _targetlist
   b.Byte "priority"
   b.Byte "unknown2"
   b.Unused 3
}

types.Label attackdata, ->(i) { return attackname[i].name }
types.AddShortcut "attackdata", attackdata[0].Location