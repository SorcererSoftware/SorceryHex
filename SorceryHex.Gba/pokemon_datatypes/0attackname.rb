types.WaitFor "_setup"

_attackname = 0x148
_attackname = 0x2E18C if types.Version == "AXVE" || types.Version == "AXPE"

self.attackname = types.ReadArray attackcount, _attackname, ->(b) {
   b.String 13, "name"
}

types.AddShortcut "attackname", attackname[0].Location