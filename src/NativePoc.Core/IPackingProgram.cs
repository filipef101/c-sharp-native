namespace NativePoc;

internal interface IPackingProgram
{
	int ItemCount { get; }

	int PackedCount { get; }

	bool Editor { get; }

	string? EditingId { get; }

	string DraftName { get; }

	string DraftNote { get; }

	bool DraftPacked { get; }

	string ItemId(int index);

	string ItemName(int index);

	bool ItemPacked(int index);

	void SetItemPacked(int index, bool packed);

	void BeginAdd();

	void BeginEdit(int index);

	void SetDraftName(string name);

	void SetDraftPacked(bool packed);

	void Save();

	void Back();

	void Copy();

	bool Apply(byte op, int itemIndex, bool argument, string? text);
}
