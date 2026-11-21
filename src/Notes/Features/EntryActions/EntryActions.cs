namespace Notes.Features.EntryActions;

/// <summary>Which action a request asks for: <paramref name="Action"/> is null if it asks for none (and its method has no default one).</summary>
/// <param name="NamesSeveral">The query has the parameters of more than one action, which is refused rather than guessed at.</param>
public sealed record ActionChoice<TAction>(TAction? Action, string Value, bool NamesSeveral) where TAction : class, IEntryAction;

/// <summary>
/// All <see cref="IEntryAction"/>s, by the method and the parameter that ask for them. Made when the app starts: two actions of a method
/// with the same parameter, or two default ones, don't let it start.
/// </summary>
public sealed class EntryActions
{
    private readonly Dictionary<Type, (Dictionary<string, IEntryAction> ByParameter, IEntryAction? Default)> _byMethod = [];

    public EntryActions(IEnumerable<IEntryAction> actions)
    {
        IEntryAction[] all = [.. actions];
        Add<IEntryGetAction>(all);
        Add<IEntryPostAction>(all);
        Add<IEntryDeleteAction>(all);
    }

    private void Add<TAction>(IEntryAction[] all) where TAction : IEntryAction
    {
        Dictionary<string, IEntryAction> byParameter = new(StringComparer.OrdinalIgnoreCase);
        IEntryAction? defaultAction = null;
        foreach (IEntryAction action in all.OfType<TAction>().Cast<IEntryAction>())
        {
            if (action.Parameter is null)
            {
                if (defaultAction is not null)
                    throw new InvalidOperationException($"{defaultAction.GetType().Name} and {action.GetType().Name} are both the default {typeof(TAction).Name}.");
                defaultAction = action;
            }
            else if (!byParameter.TryAdd(action.Parameter, action))
                throw new InvalidOperationException($"{byParameter[action.Parameter].GetType().Name} and {action.GetType().Name} are both asked for with ?{action.Parameter} ({typeof(TAction).Name}).");
        }

        _byMethod[typeof(TAction)] = (byParameter, defaultAction);
    }

    /// <summary>The action of the method (<typeparamref name="TAction"/>) that the query asks for, or its default one if it asks for none.</summary>
    public ActionChoice<TAction> Choose<TAction>(IQueryCollection query) where TAction : class, IEntryAction
    {
        (Dictionary<string, IEntryAction> byParameter, IEntryAction? defaultAction) = _byMethod[typeof(TAction)];
        string[] asked = [.. query.Keys.Where(byParameter.ContainsKey)];
        return asked.Length switch
        {
            0 => new((TAction?)defaultAction, "", NamesSeveral: false),
            1 => new((TAction)byParameter[asked[0]], query[asked[0]].ToString(), NamesSeveral: false),
            _ => new(null, "", NamesSeveral: true)
        };
    }
}
