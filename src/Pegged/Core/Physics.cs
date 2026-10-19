using System;

namespace Pegged.Core;

/// <summary>
/// A damped spring, integrated the way the original's SwiftUI spring behaves:
/// Hooke's law plus a damping term, stepped semi implicitly so it stays stable
/// at any frame rate. Half of what makes the line feel like an object.
/// </summary>
public sealed class Spring
{
    public double Value { get; set; }

    public double Target { get; set; }

    public double Velocity { get; private set; }

    /// <summary>How strongly it pulls towards the target.</summary>
    public double Stiffness { get; set; } = 46;

    /// <summary>How quickly the movement dies down.</summary>
    public double Damping { get; set; } = 2.6;

    public double Mass { get; set; } = 1;

    public bool AtRest => Math.Abs(Value - Target) < 0.05 && Math.Abs(Velocity) < 0.05;

    public Spring(double value, double target = 0, double stiffness = 46, double damping = 2.6)
    {
        Value = value;
        Target = target;
        Stiffness = stiffness;
        Damping = damping;
    }

    public void SnapTo(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    /// <summary>One step. dt is the real elapsed time, so a slow frame moves
    /// the spring further instead of slowing it down.</summary>
    public void Step(double dt)
    {
        // Long frames are split, or a stall would launch the spring.
        var remaining = Math.Min(dt, 0.1);
        const double step = 1.0 / 240.0;

        while (remaining > 0)
        {
            var h = Math.Min(step, remaining);
            remaining -= h;

            var force = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += force / Mass * h;
            Value += Velocity * h;
        }
    }
}

/// <summary>
/// A pendulum hanging from the line: the card's tilt, driven by gravity and
/// damped by air. Hang a capture on the line and it swings in; nudge it and it
/// keeps rocking for a moment. This is the other half of the feel.
/// </summary>
public sealed class Pendulum
{
    /// <summary>Gravity, tuned for degrees rather than metres.</summary>
    private const double Gravity = 980;

    /// <summary>Length of the imaginary string: shorter sways faster.</summary>
    private const double Length = 120;

    /// <summary>The card's angle away from vertical, in degrees.</summary>
    public double Angle { get; private set; }

    public double AngularVelocity { get; private set; }

    /// <summary>The resting crookedness every card keeps.</summary>
    public double Rest { get; set; }

    public double Damping { get; set; } = 2.4;

    public bool AtRest => Math.Abs(Angle - Rest) < 0.02 && Math.Abs(AngularVelocity) < 0.05;

    public void Nudge(double degrees)
    {
        Angle = Rest + degrees;
        AngularVelocity = 0;
    }

    /// <summary>Pushes the card and lets it rock: what "a breeze" does.</summary>
    public void Impulse(double degreesPerSecond)
    {
        AngularVelocity += degreesPerSecond;
    }

    public void SnapToRest()
    {
        Angle = Rest;
        AngularVelocity = 0;
    }

    public void Step(double dt)
    {
        var remaining = Math.Min(dt, 0.1);
        const double step = 1.0 / 240.0;

        while (remaining > 0)
        {
            var h = Math.Min(step, remaining);
            remaining -= h;

            // alpha = -(g/L) sin(theta) - c * omega
            var alpha = -(Gravity / Length) * Math.Sin((Angle - Rest) * Math.PI / 180) / 12.0
                        - Damping * AngularVelocity;
            AngularVelocity += alpha * h;
            Angle += AngularVelocity * h;
        }
    }
}

/// <summary>
/// One place the 60fps heartbeat comes from. Every window and every card
/// subscribes here instead of starting a timer of its own, so all of them move
/// on the same frame.
/// </summary>
public static class FrameLoop
{
    private static readonly List<Action<double>> Subscribers = new();
    private static DateTime _last;

    public static void Start()
    {
        if (Subscribers.Count > 0 && _last != default) return;

        _last = DateTime.UtcNow;
        System.Windows.Media.CompositionTarget.Rendering += OnRendering;
    }

    public static void Stop()
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
    }

    public static void Subscribe(Action<double> step)
    {
        if (!Subscribers.Contains(step)) Subscribers.Add(step);
        Start();
    }

    public static void Unsubscribe(Action<double> step)
    {
        Subscribers.Remove(step);
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _last).TotalSeconds;
        _last = now;
        if (dt <= 0 || dt > 0.5) return;

        // Copy: a subscriber may add or remove itself while stepping.
        var snapshot = Subscribers.ToArray();
        foreach (var step in snapshot) step(dt);
    }
}
