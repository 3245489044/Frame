namespace CSG.Unified;

public interface ITransformEngine
{

    CanonicalForm ConfigToCanonical(IConfiguration config);

    TConfig CanonicalToConfig<TConfig>(CanonicalForm form)
        where TConfig : IConfiguration, new();


    CanonicalForm StateMachineToCanonical(IStateMachine sm);

    IStateMachine CanonicalToStateMachine(CanonicalForm form);


    CanonicalForm GraphToCanonical(IGraph graph);

    IGraph CanonicalToGraph(CanonicalForm form);


    ValidationResult ValidateConsistency(
        CanonicalForm configForm,
        CanonicalForm stateMachineForm,
        CanonicalForm graphForm);


    List<TransformOperation> ListOperations();

    List<TransformOperation> ListOperationsByCategory(string category);

    object CallOperation(string operationName, object input);
}
