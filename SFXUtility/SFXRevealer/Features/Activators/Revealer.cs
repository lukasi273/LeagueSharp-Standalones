#region License

/*
 Copyright 2014 - 2015 Nikita Bernthaler
 Revealer.cs is part of SFXRevealer.

 SFXRevealer is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 SFXRevealer is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with SFXRevealer. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion License

#region

using System;
using System.Collections.Generic;
using System.Linq;
using LeagueSharp;
using LeagueSharp.Common;
using SFXRevealer.Classes;
using SFXRevealer.Library;
using SFXRevealer.Library.Logger;
using SharpDX;
using ItemData = LeagueSharp.Common.Data.ItemData;

#endregion

namespace SFXRevealer.Features.Activators
{
    internal class Revealer : Child<App>
    {
        private const float CheckInterval = 333f;
        private const float MaxRange = 600f;
        private const float Delay = 2f;
        private readonly List<ChampionObject> _championObjects = new List<ChampionObject>();
        private float _lastCheck = Environment.TickCount;
        private float _lastReveal;
        private Obj_AI_Hero _leBlanc;
        private Obj_AI_Hero _rengar;
        private Obj_AI_Hero _vayne;
        private HashSet<SpellData> spellList = new HashSet<SpellData>();

        public Revealer(App parent) : base(parent)
        {
            OnLoad();
        }

        public override string Name
        {
            get { return Global.Lang.Get("F_Revealer"); }
        }

        protected override void OnEnable()
        {
            GameObject.OnCreate += OnGameObjectCreate;
            Obj_AI_Base.OnProcessSpellCast += OnObjAiBaseProcessSpellCast;
            Game.OnUpdate += OnGameUpdate;

            base.OnEnable();
        }

        protected override void OnDisable()
        {
            GameObject.OnCreate -= OnGameObjectCreate;
            Obj_AI_Base.OnProcessSpellCast -= OnObjAiBaseProcessSpellCast;
            Game.OnUpdate -= OnGameUpdate;

            base.OnDisable();
        }

        protected override sealed void OnLoad()
        {
            try
            {
                Menu = new Menu(Name, Name);

                Menu.AddItem(new MenuItem(Name + "Bush", Global.Lang.Get("Revealer_Bush")).SetValue(false));
                Menu.AddItem(
                    new MenuItem(Name + "Hotkey", Global.Lang.Get("G_Hotkey")).SetValue(
                        new KeyBind(32, KeyBindType.Press)));
                Menu.AddItem(new MenuItem(Name + "Enabled", Global.Lang.Get("G_Enabled")).SetValue(false));

                Parent.Menu.AddSubMenu(Menu);
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        protected override void OnInitialize()
        {
            try
            {
                spellList = new HashSet<SpellData>
                {
                    new SpellData("Akali", SpellSlot.W),
                    new SpellData("Rengar", SpellSlot.R, true),
                    new SpellData("KhaZix", SpellSlot.R),
                    new SpellData("KhaZix", SpellSlot.R, false, "khazixrlong"),
                    new SpellData("Monkeyking", SpellSlot.W),
                    new SpellData("Shaco", SpellSlot.Q),
                    new SpellData("Talon", SpellSlot.R),
                    new SpellData("LeBlanc", SpellSlot.R, true),
                    new SpellData("Vayne", SpellSlot.Q, true),
                    new SpellData("Twitch", SpellSlot.Q)
                };

                foreach (var enemy in GameObjects.EnemyHeroes)
                {
                    _championObjects.Add(new ChampionObject(enemy));
                }

                var menuList =
                    spellList.OrderBy(s => s.Hero).GroupBy(s => s.Hero).Select(h => new { Hero = h.Key }).ToList();

                var invisibleMenu = new Menu(Global.Lang.Get("Revealer_Invisible"), Name + "Invisible");
                foreach (var spell in menuList)
                {
                    invisibleMenu.AddItem(
                        new MenuItem(invisibleMenu.Name + spell.Hero.ToLower(), spell.Hero).SetValue(true));
                }
                Menu.AddSubMenu(invisibleMenu);

                _rengar =
                    GameObjects.EnemyHeroes.FirstOrDefault(
                        e => e.ChampionName.Equals("Rengar", StringComparison.OrdinalIgnoreCase));
                _vayne =
                    GameObjects.EnemyHeroes.FirstOrDefault(
                        e => e.ChampionName.Equals("Vayne", StringComparison.OrdinalIgnoreCase));
                _leBlanc =
                    GameObjects.EnemyHeroes.FirstOrDefault(
                        e => e.ChampionName.Equals("Leblanc", StringComparison.OrdinalIgnoreCase));

                base.OnInitialize();
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnGameUpdate(EventArgs args)
        {
            if (_lastCheck + CheckInterval > Environment.TickCount)
            {
                return;
            }

            _lastCheck = Environment.TickCount;

            foreach (var championObject in _championObjects.Where(c => c.Hero.IsVisible))
            {
                championObject.LastSeen = Game.Time;
            }
            if (!Menu.Item(Name + "Bush").GetValue<bool>() || !Menu.Item(Name + "Hotkey").GetValue<KeyBind>().Active)
            {
                return;
            }

            foreach (var championObject in
                _championObjects.Where(
                    c =>
                        !c.Hero.IsVisible && !c.Hero.IsDead && Game.Time - c.LastSeen <= 2 &&
                        c.Hero.Distance(ObjectManager.Player) < 1000))
            {
                var pos = GetWardPos(championObject.Hero.ServerPosition, 165, 2);
                if (!pos.Equals(championObject.Hero.ServerPosition) && !pos.Equals(Vector3.Zero))
                {
                    CastLogic(pos, true);
                }
            }
        }

        private Vector3 GetWardPos(Vector3 lastPos, int radius = 165, int precision = 3)
        {
            var count = precision;
            while (count > 0)
            {
                var vertices = radius;
                var wardLocations = new WardLocation[vertices];
                var angle = 2 * Math.PI / vertices;
                for (var i = 0; i < vertices; i++)
                {
                    var th = angle * i;
                    var pos = new Vector3(
                        (float) (lastPos.X + radius * Math.Cos(th)), (float) (lastPos.Y + radius * Math.Sin(th)), 0);
                    wardLocations[i] = new WardLocation(pos, NavMesh.IsWallOfGrass(pos, 10));
                }
                var grassLocations = new List<GrassLocation>();
                for (var i = 0; i < wardLocations.Length; i++)
                {
                    if (wardLocations[i].Grass)
                    {
                        if (i != 0 && wardLocations[i - 1].Grass)
                        {
                            grassLocations.Last().Count++;
                        }
                        else
                        {
                            grassLocations.Add(new GrassLocation(i, 1));
                        }
                    }
                }
                var grassLocation = grassLocations.OrderByDescending(x => x.Count).FirstOrDefault();
                if (grassLocation != null)
                {
                    var midelement = (int) Math.Ceiling(grassLocation.Count / 2f);
                    lastPos = wardLocations[grassLocation.Index + midelement - 1].Pos;
                    radius = (int) Math.Floor(radius / 2f);
                }
                count--;
            }
            return lastPos;
        }

        private void CastLogic(Vector3 pos, bool bush)
        {
            try
            {
                if (pos.Distance(ObjectManager.Player.Position) > MaxRange || _lastReveal + Delay > Game.Time)
                {
                    return;
                }
                //if (!bush)
                //{
                //    if (
                //        GameObjects.AllyMinions.Any(
                //            m =>
                //                !string.IsNullOrEmpty(m.CharData.Name) &&
                //                m.CharData.Name.Equals("VisionWard", StringComparison.OrdinalIgnoreCase) &&
                //                ObjectManager.Player.Distance(m) < 400f))
                //    {
                //        return;
                //    }
                //}
                var slot = GetRevealSlot(bush);
                if (slot != SpellSlot.Unknown)
                {
                    ObjectManager.Player.Spellbook.CastSpell(slot, pos);
                    _lastReveal = Game.Time;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private SpellSlot GetRevealSlot(bool bush)
        {
            try
            {
                if (!bush)
                {
                    if (ItemData.Oracles_Lens_Trinket.GetItem().IsOwned() &&
                        ItemData.Oracles_Lens_Trinket.GetItem().IsReady())
                    {
                        return ItemData.Oracles_Lens_Trinket.GetItem().Slots.FirstOrDefault();
                    }
                    if (ItemData.Greater_Vision_Totem_Trinket.GetItem().IsOwned() &&
                        ItemData.Greater_Vision_Totem_Trinket.GetItem().IsReady())
                    {
                        return ItemData.Greater_Vision_Totem_Trinket.GetItem().Slots.FirstOrDefault();
                    }
                    if (ItemData.Vision_Ward.GetItem().IsOwned() && ItemData.Vision_Ward.GetItem().IsReady())
                    {
                        return ItemData.Vision_Ward.GetItem().Slots.FirstOrDefault();
                    }
                }
                else
                {
                    if (ItemData.Warding_Totem_Trinket.GetItem().IsOwned() &&
                        ItemData.Warding_Totem_Trinket.GetItem().IsReady())
                    {
                        return ItemData.Warding_Totem_Trinket.GetItem().Slots.FirstOrDefault();
                    }
                    if (ItemData.Greater_Stealth_Totem_Trinket.GetItem().IsOwned() &&
                        ItemData.Greater_Stealth_Totem_Trinket.GetItem().IsReady())
                    {
                        return ItemData.Greater_Stealth_Totem_Trinket.GetItem().Slots.FirstOrDefault();
                    }
                    if (ItemData.Scrying_Orb_Trinket.GetItem().IsOwned() &&
                        ItemData.Scrying_Orb_Trinket.GetItem().IsReady())
                    {
                        return ItemData.Scrying_Orb_Trinket.GetItem().Slots.FirstOrDefault();
                    }
                    if (ItemData.Farsight_Orb_Trinket.GetItem().IsOwned() &&
                        ItemData.Farsight_Orb_Trinket.GetItem().IsReady())
                    {
                        return ItemData.Farsight_Orb_Trinket.GetItem().Slots.FirstOrDefault();
                    }
                    if (ItemData.Stealth_Ward.GetItem().IsOwned() && ItemData.Stealth_Ward.GetItem().IsReady())
                    {
                        return ItemData.Stealth_Ward.GetItem().Slots.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return SpellSlot.Unknown;
        }

        private void OnObjAiBaseProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            try
            {
                var hero = sender as Obj_AI_Hero;
                if (!sender.IsEnemy || hero == null || !Menu.Item(Name + "Hotkey").GetValue<KeyBind>().Active)
                {
                    return;
                }
                var spell =
                    spellList.FirstOrDefault(
                        s =>
                            !string.IsNullOrEmpty(s.Name) &&
                            s.Name.Equals(args.SData.Name, StringComparison.OrdinalIgnoreCase));
                if (spell != null && !spell.Custom && Menu.Item(Name + "Invisibleshaco").GetValue<bool>())
                {
                    CastLogic(args.End, false);
                }

                if (_vayne != null && spell != null &&
                    spell.Hero.Equals(_vayne.ChampionName, StringComparison.OrdinalIgnoreCase) &&
                    Menu.Item(Name + "Invisible" + spell.Hero.ToLower()).GetValue<bool>())
                {
                    var buff =
                        _vayne.Buffs.FirstOrDefault(
                            b => b.Name.Equals("VayneInquisition", StringComparison.OrdinalIgnoreCase));
                    if (buff != null)
                    {
                        CastLogic(args.End, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnGameObjectCreate(GameObject sender, EventArgs args)
        {
            try
            {
                if (!sender.IsEnemy || !Menu.Item(Name + "Hotkey").GetValue<KeyBind>().Active)
                {
                    return;
                }
                if (_rengar != null && Menu.Item(Menu.Name + "Invisiblerengar").GetValue<bool>())
                {
                    if (sender.Name.Contains("Rengar_Base_R_Alert"))
                    {
                        if (ObjectManager.Player.HasBuff("rengarralertsound") && !_rengar.IsVisible && !_rengar.IsDead)
                        {
                            CastLogic(ObjectManager.Player.Position, false);
                        }
                    }
                }
                if (_leBlanc != null && Menu.Item(Menu.Name + "Invisibleleblanc").GetValue<bool>())
                {
                    if (sender.Name == "LeBlanc_Base_P_poof.troy" &&
                        ObjectManager.Player.Distance(sender.Position) <= MaxRange)
                    {
                        if (!_leBlanc.IsVisible && !_leBlanc.IsDead)
                        {
                            CastLogic(ObjectManager.Player.Position, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        internal class SpellData
        {
            public SpellData(string hero, SpellSlot slot, bool custom = false, string name = null)
            {
                try
                {
                    Hero = hero;
                    Slot = slot;
                    Custom = custom;
                    if (name != null)
                    {
                        Name = name;
                    }
                    else if (slot != SpellSlot.Unknown)
                    {
                        var champ =
                            GameObjects.EnemyHeroes.FirstOrDefault(
                                h => h.ChampionName.Equals(hero, StringComparison.OrdinalIgnoreCase));
                        if (champ != null)
                        {
                            var spell = champ.GetSpell(Slot);
                            if (spell != null)
                            {
                                Name = spell.Name;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.Logger.AddItem(new LogItem(ex));
                }
            }

            public string Hero { get; private set; }
            public SpellSlot Slot { get; private set; }
            public string Name { get; private set; }
            public bool Custom { get; private set; }
        }

        internal class ChampionObject
        {
            public ChampionObject(Obj_AI_Hero hero)
            {
                Hero = hero;
            }

            public Obj_AI_Hero Hero { get; private set; }
            public float LastSeen { get; set; }
        }

        internal class GrassLocation
        {
            public readonly int Index;
            public int Count;

            public GrassLocation(int index, int count)
            {
                Index = index;
                Count = count;
            }
        }

        internal class WardLocation
        {
            public readonly bool Grass;
            public readonly Vector3 Pos;

            public WardLocation(Vector3 pos, bool grass)
            {
                Pos = pos;
                Grass = grass;
            }
        }
    }
}