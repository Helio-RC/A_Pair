namespace SeatFlow.Core.Tests.Strategies;

public class RandomFillStrategyTests
{
    private static List<Student> CreateStudents (int count)
    {
        return [.. Enumerable.Range(1 , count).Select(i => new Student { Id = $"s{i}" , Name = $"Student{i}" })];
    }

    private static List<Seat> CreateGridSeats (int rows , int cols)
    {
        var seats = new List<Seat>();
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                seats.Add(new GridSeat { Id = $"seat_{r}_{c}" , Row = r , Column = c });
        return seats;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFillAllEmptySeats_WhenEnoughStudents ()
    {
        var students = CreateStudents(5);
        var seats = CreateGridSeats(2 , 3); // 6 seats
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy();
        var result = await strategy.ExecuteAsync(ws , CancellationToken.None);

        result.Success.Should().BeTrue();
        ws.GetEmptySeats().Count().Should().Be(1); // 5 filled, 1 empty
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotAssignMoreThanStudents ()
    {
        var students = CreateStudents(2);
        var seats = CreateGridSeats(3 , 3);
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        ws.BuildSeatingPlan().Assignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldThrow ()
    {
        var students = CreateStudents(100);
        var seats = CreateGridSeats(10 , 10);
        var ws = new SeatingWorkspace(students , seats);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var strategy = new RandomFillStrategy();
        await strategy.Awaiting(s => s.ExecuteAsync(ws , cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ValidateConfiguration_AlwaysValid ()
    {
        var strategy = new RandomFillStrategy();
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullRandom_ShouldThrowArgumentNullException ()
    {
        var act = () => new RandomFillStrategy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new RandomFillStrategy();
        var act = async () => await strategy.ExecuteAsync(null! , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyStudents_ShouldNotAssign ()
    {
        var seats = CreateGridSeats(2 , 3);
        var ws = new SeatingWorkspace(Array.Empty<Student>() , seats);

        var strategy = new RandomFillStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
        ws.GetEmptySeats().Should().HaveCount(6);
    }

    [Fact]
    public async Task ExecuteAsync_EmptySeats_ShouldNotAssign ()
    {
        var students = CreateStudents(3);
        var ws = new SeatingWorkspace(students , Array.Empty<Seat>());

        var strategy = new RandomFillStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        ws.BuildSeatingPlan().Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PartiallyAssigned_ShouldOnlyFillRemaining ()
    {
        var students = CreateStudents(5);
        var seats = CreateGridSeats(2 , 3); // 6 seats
        var ws = new SeatingWorkspace(students , seats);

        // Pre-assign 2 students
        ws.TryAssignSeat(seats[0].Id , students[0].Id , out _);
        ws.TryAssignSeat(seats[1].Id , students[1].Id , out _);

        var strategy = new RandomFillStrategy();
        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // 2 pre-assigned + 3 remaining students = 5 total assigned
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(5);
        ws.GetEmptySeats().Should().HaveCount(1);
    }

    // ═══════════ 依赖策略上下文测试 ═══════════

    [Fact]
    public void LoadDependentStrategies_ShouldSetHasActiveDependents ()
    {
        var strategy = new RandomFillStrategy();
        strategy.HasActiveDependents.Should().BeFalse();

        var dep = new MockDependent("dep1" , 90 , DependentResult.Approve());
        strategy.LoadDependentStrategies([dep]);

        strategy.HasActiveDependents.Should().BeTrue();
    }

    [Fact]
    public void LoadDependentStrategies_DisabledDependent_ShouldNotCountAsActive ()
    {
        var strategy = new RandomFillStrategy();
        var dep = new MockDependent("dep1" , 90 , DependentResult.Approve()) { IsEnabled = false };
        strategy.LoadDependentStrategies([dep]);

        strategy.HasActiveDependents.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithApprovingDependent_ShouldAssign ()
    {
        var students = CreateStudents(3);
        var seats = CreateGridSeats(1 , 3);
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy(new Random(42));
        var dep = new MockDependent("dep1" , 90 , DependentResult.Approve());
        strategy.LoadDependentStrategies([dep]);

        await strategy.ExecuteAsync(ws , CancellationToken.None);

        dep.EvaluateCallCount.Should().BeGreaterThan(0);
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithHandlingDependent_ShouldSkipTryAssignSeat ()
    {
        var students = CreateStudents(3);
        var seats = CreateGridSeats(1 , 3);
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy(new Random(42));
        // Handles: dependent actually assigns the student + mate, RandomFill should skip TryAssignSeat
        var dep = new AssigningDependent("dep1" , 90);
        strategy.LoadDependentStrategies([dep]);

        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // All should be assigned (Handled by dependent)
        ws.BuildSeatingPlan().Assignments.Should().HaveCount(3);
        dep.EvaluateCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_DependentStrategyPriorityOrder_ShouldCallInOrder ()
    {
        var students = CreateStudents(3);
        var seats = CreateGridSeats(1 , 3);
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy(new Random(42));
        var callOrder = new List<string>();
        var dep1 = new TracingDependent("dep1" , 10 , callOrder);
        var dep2 = new TracingDependent("dep2" , 20 , callOrder); // higher priority (higher number)
        strategy.LoadDependentStrategies([dep1 , dep2]);

        await strategy.ExecuteAsync(ws , CancellationToken.None);

        // dep2 (priority 20) should be called before dep1 (priority 10)
        if (callOrder.Count >= 2)
        {
            var firstDep2 = callOrder.IndexOf("dep2");
            var firstDep1 = callOrder.IndexOf("dep1");
            firstDep2.Should().BeLessThan(firstDep1);
        }
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDependentList_ShouldUseFastPath ()
    {
        var students = CreateStudents(5);
        var seats = CreateGridSeats(2 , 3);
        var ws = new SeatingWorkspace(students , seats);

        var strategy = new RandomFillStrategy(new Random(42));
        // No dependents loaded → fast path

        await strategy.ExecuteAsync(ws , CancellationToken.None);

        ws.BuildSeatingPlan().Assignments.Should().HaveCount(5);
    }

    // ═══════════ Mock 实现 ═══════════

    /// <summary>Mock 依赖策略，返回预设的评估结果。</summary>
    private sealed class MockDependent (string id , int priority , DependentEvaluationResult presetResult) : IDependentSeatingStrategy
    {
        private readonly DependentEvaluationResult _presetResult = presetResult;

        public string Id { get; } = id;
        public string Name { get; } = id;
        public string DisplayName { get; } = id;
        public int Priority { get; set; } = priority;
        public bool IsEnabled { get; set; } = true;
        public int EvaluateCallCount { get; private set; }

        public Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace , Student student , Seat targetSeat ,
            IRandomFillContext context , CancellationToken cancellationToken)
        {
            EvaluateCallCount++;
            return Task.FromResult(_presetResult);
        }

        public ValidationResult ValidateConfiguration () => new() { IsValid = true };
    }

    /// <summary>记录调用顺序的依赖策略（始终 Approve）。</summary>
    private sealed class TracingDependent (string id , int priority , List<string> callOrder) : IDependentSeatingStrategy
    {
        private readonly List<string> _callOrder = callOrder;

        public string Id { get; } = id;
        public string Name { get; } = id;
        public string DisplayName { get; } = id;
        public int Priority { get; set; } = priority;
        public bool IsEnabled { get; set; } = true;

        public Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace , Student student , Seat targetSeat ,
            IRandomFillContext context , CancellationToken cancellationToken)
        {
            _callOrder.Add(Id);
            return Task.FromResult(DependentResult.Approve());
        }

        public ValidationResult ValidateConfiguration () => new() { IsValid = true };
    }

    /// <summary>实际执行分配的依赖策略（Handled 场景）。</summary>
    private sealed class AssigningDependent (string id , int priority) : IDependentSeatingStrategy
    {
        public string Id { get; } = id;
        public string Name { get; } = id;
        public string DisplayName { get; } = id;
        public int Priority { get; set; } = priority;
        public bool IsEnabled { get; set; } = true;
        public int EvaluateCallCount { get; private set; }

        public Task<DependentEvaluationResult> EvaluateAsync (
            SeatingWorkspace workspace , Student student , Seat targetSeat ,
            IRandomFillContext context , CancellationToken cancellationToken)
        {
            EvaluateCallCount++;
            // Actually assign the student (simulating a real dependent strategy)
            workspace.TryAssignSeat(targetSeat.Id , student.Id , out _);
            return Task.FromResult(DependentResult.Handled());
        }

        public ValidationResult ValidateConfiguration () => new() { IsValid = true };
    }
}