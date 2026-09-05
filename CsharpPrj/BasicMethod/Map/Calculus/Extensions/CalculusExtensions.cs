namespace BasicMethod.Calculus.Extensions
{
    public static class CalculusExtensions
    {
        public static T Pipe<T>(
            this IDifferentiator<T> differentiator,
            IIntegrator<T> integrator,
            params T[] inputs)
        {
            if (differentiator == null) throw new ArgumentNullException(nameof(differentiator));
            if (integrator == null) throw new ArgumentNullException(nameof(integrator));

            var result = integrator.Reset();
            foreach (var input in inputs)
            {
                var diffResult = differentiator.Differentiate(input);
                result = integrator.Integrate(result, diffResult);
            }
            return result;
        }

        public static T EvolveUntilConverged<T>(
            this IEvolver<T> evolver,
            IConverger<T> converger,
            T initial,
            int maxIterations = 100)
        {
            if (evolver == null) throw new ArgumentNullException(nameof(evolver));
            if (converger == null) throw new ArgumentNullException(nameof(converger));

            var current = initial;
            var previous = default(T);

            for (int i = 0; i < maxIterations; i++)
            {
                previous = current;
                current = evolver.Evolve(current);
                current = converger.Converge(current);

                if (converger.HasConverged(current, previous))
                    break;
            }

            return current;
        }
    }
}