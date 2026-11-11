using NativePoc;

var value = ReloadDispatch.Call(
	"NativePoc.ReloadAot.Banner",
	"Label",
	[RecordProgram.AppValue.FromStr("Ada")]);
return value.Text == "rebuilt" ? 0 : 1;
