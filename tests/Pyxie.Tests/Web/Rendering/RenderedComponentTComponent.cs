using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;

using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A component that was rendered to HTML, with its instance, so a test can read its markup, call its public methods, and read the markup again.</summary>
/// <typeparam name="TComponent">The component type.</typeparam>
internal sealed class RenderedComponent<TComponent>
	where TComponent : IComponent
{
	// Some pages wait for real work, such as an import that clones and creates a database, which can take far longer on a busy machine; a wait that succeeds returns at once.
	private static readonly TimeSpan s_giveUp = RuntimeTestTimeouts.Wait;
	private static readonly TimeSpan s_pollEvery = TimeSpan.FromMilliseconds(5);

	private readonly HtmlRenderer _renderer;
	private readonly HtmlRootComponent _root;

	/// <summary>Initializes a new instance of the <see cref="RenderedComponent{TComponent}"/> class.</summary>
	/// <param name="renderer">The renderer the component was rendered by.</param>
	/// <param name="root">The rendered root.</param>
	/// <param name="instance">The component instance.</param>
	public RenderedComponent(HtmlRenderer renderer, HtmlRootComponent root, TComponent instance)
	{
		_renderer = renderer;
		_root = root;
		Instance = instance;
	}

	/// <summary>Gets the component instance.</summary>
	public TComponent Instance { get; }

	/// <summary>Reads the component's markup as it is now.</summary>
	/// <returns>The HTML.</returns>
	public Task<string> HtmlAsync()
	{
		return _renderer.Dispatcher.InvokeAsync(_root.ToHtmlString);
	}

	/// <summary>Runs work on the component's context, as an event handler would, and lets the render it causes finish.</summary>
	/// <param name="work">The work to run.</param>
	/// <returns>A task that completes when the work has.</returns>
	public Task RunAsync(Func<TComponent, Task> work)
	{
		return _renderer.Dispatcher.InvokeAsync(() => work(Instance));
	}

	/// <summary>Runs work on the component's context, as an event handler would, and lets the render it causes finish.</summary>
	/// <param name="work">The work to run.</param>
	/// <returns>A task that completes when the work has.</returns>
	public Task RunAsync(Action<TComponent> work)
	{
		return _renderer.Dispatcher.InvokeAsync(() => work(Instance));
	}

	/// <summary>Does what a click on a button, or an event a child component raises, does: runs the component's private handler of that name on the component's context, and renders the component again afterwards, as the framework does after an event handler. The static render has no browser to click in, so a test names the handler the button or the child is wired to.</summary>
	/// <param name="handler">The name of the handler method, which returns a task or nothing.</param>
	/// <param name="arguments">What the event passes the handler, such as the value a modal reports; none for a click.</param>
	/// <returns>A task that completes when the handler has, and the render after it.</returns>
	public Task ClickAsync(string handler, params object?[] arguments)
	{
		MethodInfo method = typeof(TComponent).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? throw new MissingMethodException(typeof(TComponent).Name, handler);
		IHandleEvent events = Instance as IHandleEvent ?? throw new InvalidOperationException($"{typeof(TComponent).Name} does not handle events.");
		Func<Task> invoke = () => method.Invoke(Instance, arguments) as Task ?? Task.CompletedTask;

		return _renderer.Dispatcher.InvokeAsync(() => events.HandleEventAsync(new EventCallbackWorkItem(invoke), null));
	}

	/// <summary>Reads a private field of the component, such as the form a person has typed into.</summary>
	/// <typeparam name="TField">The type of the field.</typeparam>
	/// <param name="name">The name of the field.</param>
	/// <returns>The field's value.</returns>
	public TField ReadField<TField>(string name)
	{
		FieldInfo field = typeof(TComponent).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
			?? throw new MissingFieldException(typeof(TComponent).Name, name);

		return (TField)(field.GetValue(Instance) ?? throw new InvalidOperationException($"{name} is empty."));
	}

	/// <summary>Waits until the markup satisfies a condition. A component that reacts to an event from another thread moves the work onto its own context and gives the test nothing to await, so the markup is read again until it shows the result.</summary>
	/// <param name="condition">The condition the markup has to satisfy.</param>
	/// <param name="failure">What the test expected to see, for the failure message.</param>
	/// <returns>The markup that satisfied the condition.</returns>
	public async Task<string> WaitForHtmlAsync(Func<string, bool> condition, string failure)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(s_giveUp);
		string html = await HtmlAsync();

		while (!condition(html))
		{
			try
			{
				await Task.Delay(s_pollEvery, deadline.Token);
			}
			catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
			{
				Assert.Fail($"{failure}. The markup stayed:{Environment.NewLine}{html}");
			}

			html = await HtmlAsync();
		}

		return html;
	}
}
