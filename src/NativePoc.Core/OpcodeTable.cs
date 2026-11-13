using System.Text;

namespace NativePoc;

internal static class OpcodeTable
{
	internal readonly record struct Row(string Name, byte Code, int Arity);

	// Arity is the fixed immediate width. A script that follows a length prefix is not counted.
	internal static readonly Row[] Rows =
	[
		new(nameof(RecordProgram.OpPushStr), RecordProgram.OpPushStr, 2),
		new(nameof(RecordProgram.OpPushBool), RecordProgram.OpPushBool, 1),
		new(nameof(RecordProgram.OpLoadStr), RecordProgram.OpLoadStr, 2),
		new(nameof(RecordProgram.OpLoadBool), RecordProgram.OpLoadBool, 2),
		new(nameof(RecordProgram.OpLoadInt), RecordProgram.OpLoadInt, 2),
		new(nameof(RecordProgram.OpConcat), RecordProgram.OpConcat, 0),
		new(nameof(RecordProgram.OpJumpIfFalse), RecordProgram.OpJumpIfFalse, 2),
		new(nameof(RecordProgram.OpJump), RecordProgram.OpJump, 2),
		new(nameof(RecordProgram.OpMakeText), RecordProgram.OpMakeText, 2),
		new(nameof(RecordProgram.OpMakeTextKey), RecordProgram.OpMakeTextKey, 0),
		new(nameof(RecordProgram.OpMakeField), RecordProgram.OpMakeField, 4),
		new(nameof(RecordProgram.OpMakeSwitch), RecordProgram.OpMakeSwitch, 4),
		new(nameof(RecordProgram.OpMakeButton), RecordProgram.OpMakeButton, 4),
		new(nameof(RecordProgram.OpMakeColumn), RecordProgram.OpMakeColumn, 3),
		new(nameof(RecordProgram.OpMakeScroll), RecordProgram.OpMakeScroll, 2),
		new(nameof(RecordProgram.OpReturn), RecordProgram.OpReturn, 0),
		new(nameof(RecordProgram.OpMark), RecordProgram.OpMark, 0),
		new(nameof(RecordProgram.OpForEach), RecordProgram.OpForEach, 4),
		new(nameof(RecordProgram.OpItemStr), RecordProgram.OpItemStr, 2),
		new(nameof(RecordProgram.OpGather), RecordProgram.OpGather, 2),
		new(nameof(RecordProgram.OpStrEmpty), RecordProgram.OpStrEmpty, 0),
		new(nameof(RecordProgram.OpMakeColumnKey), RecordProgram.OpMakeColumnKey, 1),
		new(nameof(RecordProgram.OpListCount), RecordProgram.OpListCount, 2),
		new(nameof(RecordProgram.OpListFlagCount), RecordProgram.OpListFlagCount, 4),
		new(nameof(RecordProgram.OpEqStr), RecordProgram.OpEqStr, 0),
		new(nameof(RecordProgram.OpItemBool), RecordProgram.OpItemBool, 2),
		new(nameof(RecordProgram.OpMakeList), RecordProgram.OpMakeList, 2),
		new(nameof(RecordProgram.OpStyle), RecordProgram.OpStyle, StyleCodec.Bytes),
		new(nameof(RecordProgram.OpMakeImage), RecordProgram.OpMakeImage, 4),
		new(nameof(RecordProgram.OpEffect), RecordProgram.OpEffect, 4),
		new(nameof(RecordProgram.OpEvery), RecordProgram.OpEvery, 2),
		new(nameof(RecordProgram.OpFieldMode), RecordProgram.OpFieldMode, 6),
		new(nameof(RecordProgram.OpMotion), RecordProgram.OpMotion, MotionCodec.Bytes),
		new(nameof(RecordProgram.OpGesture), RecordProgram.OpGesture, 3),
		new(nameof(RecordProgram.OpHostEvent), RecordProgram.OpHostEvent, 4),
		new(nameof(RecordProgram.OpRes), RecordProgram.OpRes, 0),
		new(nameof(RecordProgram.OpPlural), RecordProgram.OpPlural, 0),
		new(nameof(RecordProgram.OpFormat), RecordProgram.OpFormat, 1),
		new(nameof(RecordProgram.OpMakeHost), RecordProgram.OpMakeHost, 2),
		new(nameof(RecordProgram.OpCall), RecordProgram.OpCall, 2),
		new(nameof(RecordProgram.OpVmBool), RecordProgram.OpVmBool, 2),
		new(nameof(RecordProgram.OpSeedList), RecordProgram.OpSeedList, 12),
		new(nameof(RecordProgram.OpOnOpen), RecordProgram.OpOnOpen, 4),
		new(nameof(RecordProgram.OpRun), RecordProgram.OpRun, 2),
		new(nameof(RecordProgram.OpOnPause), RecordProgram.OpOnPause, 2),
		new(nameof(RecordProgram.OpOnResume), RecordProgram.OpOnResume, 2),
		new(nameof(RecordProgram.OpOnLeave), RecordProgram.OpOnLeave, 2),
		new(nameof(RecordProgram.OpMakeTextInput), RecordProgram.OpMakeTextInput, 4),
	];

	internal static uint Version => Stamp(Rows);

	internal static uint Stamp(ReadOnlySpan<Row> rows)
	{
		const uint offset = 2166136261;
		const uint prime = 16777619;
		var hash = offset;
		foreach (var row in rows)
		{
			hash = Mix(hash, row.Name, prime);
			hash = Mix(hash, row.Code, prime);
			hash = Mix(hash, row.Arity, prime);
		}

		return hash;
	}

	static uint Mix(uint hash, string text, uint prime)
	{
		foreach (var unit in Encoding.UTF8.GetBytes(text))
			hash = (hash ^ unit) * prime;
		hash = (hash ^ (byte)'\n') * prime;
		return hash;
	}

	static uint Mix(uint hash, int value, uint prime)
	{
		hash = (hash ^ (byte)value) * prime;
		hash = (hash ^ (byte)(value >> 8)) * prime;
		hash = (hash ^ (byte)(value >> 16)) * prime;
		hash = (hash ^ (byte)(value >> 24)) * prime;
		hash = (hash ^ (byte)'\n') * prime;
		return hash;
	}
}
