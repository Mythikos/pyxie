namespace Pyxie.Web.Components.Board;

/// <summary>What the card dialog offers for the card it shows.</summary>
internal enum CardEditorMode
{
	/// <summary>A new card is being written.</summary>
	Create,

	/// <summary>A card that is still in the Queue is being edited.</summary>
	Edit,

	/// <summary>A card an agent has picked up, or one that is closed, is shown read-only.</summary>
	Review,

	/// <summary>The card no longer exists. The dialog keeps what it showed and offers only to go back.</summary>
	Gone,
}
