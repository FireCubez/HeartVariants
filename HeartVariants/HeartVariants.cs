using Celeste.Mod;
using Celeste.Mod.UI;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using YamlDotNet.Serialization;

namespace Celeste.Mod.HeartVariants;

public class HeartVariants : EverestModule
{
	private ILHook collectRoutineHook;

	public override void Load()
	{
		collectRoutineHook = new ILHook(typeof(HeartGem).GetMethod("CollectRoutine", BindingFlags.NonPublic | BindingFlags.Instance).GetStateMachineTarget(), CollectRoutineHook);
	}

	public override void Unload()
	{
		collectRoutineHook.Dispose();
	}

	public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot)
	{
		base.CreateModMenuSection(menu, inGame, snapshot);

		menu.Add(new TextMenu.SubHeader("Heart Variants"));

		foreach (var variant in Variants.GetVariants())
		{
			variant.AddToMenu(menu);
		}
		menu.Add(new TextMenu.Button("Reset all Heart Variants to default values").Pressed(() =>
		{
			foreach (var variant in Variants.GetVariants())
			{
				variant.ResetToDefault();
			}
		}));
	}

	private void CollectRoutineHook(ILContext il)
	{
		ILCursor cursor = new(il);

		cursor.GotoNext(MoveType.After, inst => inst.MatchNewobj<AbsorbOrb>());
		cursor.EmitDelegate(ModifyAbsorbOrb);
		cursor.GotoNext(MoveType.Before, inst => inst.MatchLdcI4(10));
		cursor.Remove();
		cursor.EmitDelegate(GetOrbCount);
	}

	private int GetOrbCount() => Variants.OrbCount.Value;

	private AbsorbOrb ModifyAbsorbOrb(AbsorbOrb orb)
	{
		CustomAbsorbOrb newOrb = new(orb.Position, orb.AbsorbInto, orb.AbsorbTarget);
		var data = DynamicData.For(newOrb);
		data.Set("consumeDelay", Variants.ConsumeDelay.Value);
		data.Set("burstSpeed", Variants.BurstSpeed.Value);
		data.Set("burstDirection",
			Calc.AngleToVector(Variants.BurstDir.Value, Variants.BurstDirLength.Value)
			+ new Vector2(Variants.BurstDirAbsX.Value, Variants.BurstDirAbsY.Value)
		);
		newOrb.OverrideDuration = true;
		newOrb.CustomDuration = Variants.Duration.Value;
		return newOrb;
	}
}

public abstract class BaseVariant
{
	public abstract void AddToMenu(TextMenu menu);
	public abstract void ResetToDefault();
}

public abstract class Variant<T> : BaseVariant
{
	public virtual T Value { get; protected set; }

	protected virtual T Default { get; set; }

	public virtual string Label { get; protected set; }

	public Variant(string l, T d)
	{
		Label = l;
		Value = Default = d;
	}

	public override void ResetToDefault() { Value = Default; }
}

public class IntVariant : Variant<int>
{
	public override int Value => Base + Calc.Random.Range(0, Rand + 1);

	public int Base;
	public int Rand;
	public int BaseDefault;
	public int RandDefault;

	public TextMenu.Button BaseButton;
	public TextMenu.Button RandButton;

	public IntVariant(string l, int db = 0, int dr = 0) : base(l, 0)
	{
		Base = BaseDefault = db;
		Rand = RandDefault = dr;
	}

	public override void AddToMenu(TextMenu menu)
	{
		menu.Add(BaseButton = new TextMenu.Button(Label + " (Base): " + Base));
		BaseButton.Pressed(() =>
		{
			Audio.Play(SFX.ui_main_savefile_rename_start);
			OuiModOptions.Instance.Overworld.Goto<OuiNumberEntry>().Init<OuiModOptions>(
				Base,
				v => Base = (int)v,
				12,
				false,
				true
			);
		});
		menu.Add(RandButton = new TextMenu.Button(Label + " (Rand): " + Rand));
		RandButton.Pressed(() =>
		{
			Audio.Play(SFX.ui_main_savefile_rename_start);
			OuiModOptions.Instance.Overworld.Goto<OuiNumberEntry>().Init<OuiModOptions>(
				Rand,
				v => Rand = (int)v,
				12,
				false,
				true
			);
		});
	}

	public override void ResetToDefault()
	{
		base.ResetToDefault();
		Base = BaseDefault;
		Rand = RandDefault;
		BaseButton.Label = Label + " (Base): " + Base;
		RandButton.Label = Label + " (Rand): " + Rand;
	}
}

public class FloatVariant : Variant<float>
{

	public override float Value => Base + Calc.Random.NextFloat() * Rand;

	public float Base;
	public float Rand;
	public float BaseDefault;
	public float RandDefault;

	public TextMenu.Button BaseButton;
	public TextMenu.Button RandButton;

	public FloatVariant(string l, float db = 0, float dr = 0) : base(l, 0) {
		Base = BaseDefault = db;
		Rand = RandDefault = dr;
	}

	public override void AddToMenu(TextMenu menu)
	{
		menu.Add(BaseButton = new TextMenu.Button(Label + " (Base): " + Base));
		BaseButton.Pressed(() =>
		{
			Audio.Play(SFX.ui_main_savefile_rename_start);
			OuiModOptions.Instance.Overworld.Goto<OuiNumberEntry>().Init<OuiModOptions>(
				Base,
				v => Base = v,
				12,
				true,
				true
			);
		});
		menu.Add(RandButton = new TextMenu.Button(Label + " (Rand): " + Rand));
		RandButton.Pressed(() =>
		{
			Audio.Play(SFX.ui_main_savefile_rename_start);
			OuiModOptions.Instance.Overworld.Goto<OuiNumberEntry>().Init<OuiModOptions>(
				Rand,
				v => Rand = v,
				12,
				true,
				true
			);
		});
	}

	public override void ResetToDefault()
	{
		base.ResetToDefault();
		Base = BaseDefault;
		Rand = RandDefault;
		BaseButton.Label = Label + " (Base): " + Base;
		RandButton.Label = Label + " (Rand): " + Rand;
	}
}

public class ColorVariant : Variant<Color>
{

	public string ValueString =>
		Value.R.ToString("X2") +
		Value.G.ToString("X2") +
		Value.B.ToString("X2") +
		Value.A.ToString("X2");

	public Color ColorFromString(string v)
	{
		if (v.Length < 8) return Default;
		byte r = byte.Parse(v.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
		byte g = byte.Parse(v.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
		byte b = byte.Parse(v.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
		byte a = byte.Parse(v.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
		return new(r, g, b, a);
	}

	public TextMenu.Button Button;

	public ColorVariant(string l, Color d) : base(l, d) { }

	public override void AddToMenu(TextMenu menu)
	{
		menu.Add(Button = new TextMenu.Button(Label + ": " + ValueString));
		Button.Pressed(() =>
		{
			Audio.Play(SFX.ui_main_savefile_rename_start);
			OuiModOptions.Instance.Overworld.Goto<OuiTextEntry>().Init<OuiModOptions>(
				ValueString,
				v => Value = ColorFromString(v)
			);
		});
	}

	public override void ResetToDefault()
	{
		base.ResetToDefault();
		Button.Label = Label + ": " + ValueString;
	}
}

public class BoolVariant : Variant<bool>
{
	public BoolVariant(string l, bool d = false) : base(l, d) { }

	public TextMenu.OnOff OnOff;

	public override void AddToMenu(TextMenu menu)
	{
		menu.Add(OnOff = new TextMenu.OnOff(Label, Default));
		OnOff.Change(v => Value = v);
	}

	public override void ResetToDefault()
	{
		base.ResetToDefault();
		OnOff.Index = Default ? 1 : 0;
	}
}

public record struct EaserInfo {
	public Ease.Easer Easer;
	public string Name;
}

public class EaserVariant : Variant<Ease.Easer>
{

	public static EaserInfo[] Easers = typeof(Ease)
		.GetFields(BindingFlags.Static | BindingFlags.Public)
		.Select(x => new EaserInfo() { Easer = (Ease.Easer)x.GetValue(null), Name = x.Name })
		.ToArray();

	public TextMenu.Slider Slider;

	public EaserVariant(string l, Ease.Easer d = null) : base(l, d) {
		if(d == null) Value = Default = Ease.Linear;
	}

	public override void AddToMenu(TextMenu menu)
	{
		int idx = 0;
		while (Easers[idx].Easer != Default) idx++;
		menu.Add(Slider = new TextMenu.Slider(Label, x => Easers[x].Name, 0, Easers.Length - 1, idx));
		Slider.Change(v => Value = Easers[v].Easer);
	}

	public override void ResetToDefault()
	{
		base.ResetToDefault();
		Slider.PreviousIndex = Slider.Index;
		Slider.Index = 0;
		while (Easers[Slider.Index].Easer != Default) Slider.Index++;
	}
}

public static class Variants
{
	public static Variant<int> OrbCount = new IntVariant("Orb Count", 10);
	public static Variant<Color> OrbColorBegin = new ColorVariant("Orb Color (Begin)", Color.White);
	public static Variant<Color> OrbColorEnd = new ColorVariant("Orb Color (End)", Color.White);
	public static Variant<float> ConsumeDelay = new FloatVariant("Consume Delay", 0.7f, 0.3f);
	public static Variant<float> BurstSpeed = new FloatVariant("Burst Speed", 80, 40);
	public static Variant<float> BurstDir = new FloatVariant("Burst Direction", 0, (float)Math.PI * 2f);
	public static Variant<float> BurstDirLength = new FloatVariant("Burst Direction Length", 1, 0);
	public static Variant<float> BurstDirAbsX = new FloatVariant("Burst X Offset", 0, 0);
	public static Variant<float> BurstDirAbsY = new FloatVariant("Burst Y Offset", 0, 0);
	public static Variant<float> Duration = new FloatVariant("Consume Duration", 0.3f, 0.25f);
	public static Variant<Ease.Easer> CustomAbsorbEase = new EaserVariant("Custom Absorb Ease", Ease.CubeIn);
	public static Variant<bool> LinearAbsorb = new BoolVariant("Non-Curved Absorb", false);
	public static Variant<float> CustomAbsorbControl = new FloatVariant("Absorb Curve Midpoint Multiplier", 1);

	public static IEnumerable<BaseVariant> GetVariants() => typeof(Variants)
			.GetFields(BindingFlags.Static | BindingFlags.Public)
			.Select(x => (BaseVariant)x.GetValue(null));
}

public class CustomAbsorbOrb : AbsorbOrb
{
	public bool OverrideDuration;
	public float CustomDuration;

	public CustomAbsorbOrb(Vector2 position, Entity into = null, Vector2? absorbTarget = null) : base(position, into, absorbTarget) {
		DynamicData.For(this).Get<Image>("sprite").Color = Variants.OrbColorBegin.Value;
	}

	public override void Update()
	{
		var data = DynamicData.For(this);

		Vector2 vector = Vector2.Zero;
		bool flag = false;
		if (AbsorbInto != null)
		{
			vector = AbsorbInto.Center;
			flag = (AbsorbInto.Scene == null || (AbsorbInto is Player && (AbsorbInto as Player).Dead));
		}
		else if (AbsorbTarget.HasValue)
		{
			vector = AbsorbTarget.Value;
		}
		else
		{
			Player entity = base.Scene.Tracker.GetEntity<Player>();
			if (entity != null)
			{
				vector = entity.Center;
			}

			flag = (entity == null || entity.Scene == null || entity.Dead);
		}

		if (flag)
		{
			Position +=
				data.Get<Vector2>("burstDirection") *
				data.Get<float>("burstSpeed") * Engine.RawDeltaTime;
			data.Set("burstSpeed", Calc.Approach(data.Get<float>("burstSpeed"), 800f, Engine.RawDeltaTime * 200f));
			var sprite = data.Get<Image>("sprite");
			sprite.Rotation = data.Get<Vector2>("burstDirection").Angle();
			sprite.Scale = new Vector2(Math.Min(2f, 0.5f + data.Get<float>("burstSpeed") * 0.02f), Math.Max(0.05f, 0.5f - data.Get<float>("burstSpeed") * 0.004f));
			float alpha;
			sprite.Color = GetOrbColor(data.Get<float>("percent")) * (alpha = Calc.Approach(data.Get<float>("alpha"), 0f, Engine.DeltaTime));
			data.Set("alpha", alpha);
		}
		else if (data.Get<float>("consumeDelay") > 0f)
		{
			Position +=
				data.Get<Vector2>("burstDirection") *
				data.Get<float>("burstSpeed") * Engine.RawDeltaTime;
			data.Set("burstSpeed", Calc.Approach(data.Get<float>("burstSpeed"), 0f, Engine.RawDeltaTime * 120f));
			var sprite = data.Get<Image>("sprite");
			sprite.Rotation = data.Get<Vector2>("burstDirection").Angle();
			sprite.Scale = new Vector2(Math.Min(2f, 0.5f + data.Get<float>("burstSpeed") * 0.02f), Math.Max(0.05f, 0.5f - data.Get<float>("burstSpeed") * 0.004f));
			data.Set("consumeDelay", data.Get<float>("consumeDelay") - Engine.RawDeltaTime);
			if (data.Get<float>("consumeDelay") <= 0f)
			{
				Vector2 position = Position;
				Vector2 vector2 = vector;
				Vector2 value = (position + vector2) / 2f;
				Vector2 value2 = (vector2 - position).SafeNormalize().Perpendicular() * (position - vector2).Length() * (0.05f + Calc.Random.NextFloat() * 0.45f);
				float value3 = vector2.X - position.X;
				float value4 = vector2.Y - position.Y;
				if ((Math.Abs(value3) > Math.Abs(value4) && Math.Sign(value2.X) != Math.Sign(value3)) || (Math.Abs(value4) > Math.Abs(value4) && Math.Sign(value2.Y) != Math.Sign(value4)))
				{
					value2 *= -1f;
				}

				data.Set("curve", new SimpleCurve(position, vector2 * Variants.CustomAbsorbControl.Value, value + value2));
				data.Set("duration", OverrideDuration ? CustomDuration : (0.3f + Calc.Random.NextFloat(0.25f)));
				data.Set("burstScale", sprite.Scale);
			}
		}
		else
		{
			var curve = data.Get<SimpleCurve>("curve");
			curve.End = vector;
			data.Set("curve", curve);
			var percent = data.Get<float>("percent");
			var duration = data.Get<float>("duration");
			var sprite = data.Get<Image>("sprite");
			if (percent >= 1f)
			{
				Audio.Play("event:/new_content/char/tutorial_ghost/jump");
				RemoveSelf();
			}

			percent = Calc.Approach(percent, 1f, Engine.RawDeltaTime / duration);
			data.Set("percent", percent);
			float num = Variants.CustomAbsorbEase.Value(percent);
			Position = CurveGetPoint(curve, num);
			float num2 = Calc.YoYo(num) * curve.GetLengthParametric(10);
			sprite.Scale = new Vector2(Math.Min(2f, 0.5f + num2 * 0.02f), Math.Max(0.05f, 0.5f - num2 * 0.004f));
			sprite.Color = GetOrbColor(percent) * (1f - num);
			sprite.Rotation = Calc.Angle(Position, CurveGetPoint(curve, Variants.CustomAbsorbEase.Value(percent + 0.01f)));
		}
	}

	private Vector2 CurveGetPoint(SimpleCurve curve, float n)
	{
		if (Variants.LinearAbsorb.Value) return new(
			Calc.LerpClamp(curve.Begin.X, curve.End.X, n),
			Calc.LerpClamp(curve.Begin.Y, curve.End.Y, n)
		);
		return curve.GetPoint(n);
	}

	private Color GetOrbColor(float percent) => Color.Lerp(
		Variants.OrbColorBegin.Value, Variants.OrbColorEnd.Value,
		percent
	);
}