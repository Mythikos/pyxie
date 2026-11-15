namespace Pyxie.Web.Components.Brand;

/// <summary>Which of the three drawings of Pyxie's mark to show. The drawings are fixed art from docs/design/assets, served unchanged from wwwroot/brand.</summary>
public enum PyxieMarkDrawing
{
	/// <summary>Eyes open: the header mark, and the pipeline status while the pipeline runs.</summary>
	Awake,

	/// <summary>Eyes closed: the pipeline status while the pipeline is stopped.</summary>
	Asleep,

	/// <summary>The drawing made to read at 16 px: the favicon, the Merging badge, and the avatar on Pyxie's merge commits.</summary>
	Small,
}
