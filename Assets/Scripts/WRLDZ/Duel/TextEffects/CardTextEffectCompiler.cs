using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Compiles official Konami/Yugipedia card text (<see cref="CardDef.desc"/>) into a
    /// <see cref="CompiledCardProgram"/> the first time a card is played.
    ///
    /// Approach: PSCT template matching (not free-form LLM). Only clauses that fully match
    /// a known template are stored — never invent partial effects.
    /// Reference style: open-source YGOPro Lua scripts (timing + operation), Unity-native C#.
    /// </summary>
    public static class CardTextEffectCompiler
    {
        public const int Version = 72;

        static readonly Regex RxDraw = new(
            @"(?:^|[.!?]\s+)Draw (\d+) cards?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllOppMonsters = new(
            @"(?<!If you call it right, )Destroy all monsters your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllMonsters = new(
            @"Destroy all monsters on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllST = new(
            @"Destroy all Spell and Trap Cards on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllOppST = new(
            @"Destroy all Spell and Trap Cards your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTargetStDestroy = new(
            @"Target 1 Spell/?Trap on the field;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMonsterReborn = new(
            @"Target 1 monster in either GY;\s*Special Summon it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipDestroyMonster = new(
            @"FLIP:\s*Target 1 monster on the field;\s*destroy it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Old Vindictive Magician / Night Assailant Flip destroy (opp only).</summary>
        static readonly Regex RxFlipDestroyOppMonster = new(
            @"FLIP:\s*Target 1 monster your opponent controls;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipSpellToHand = new(
            @"FLIP:\s*Target 1 Spell in your GY;\s*add that target to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mask of Darkness: Trap in GY to hand (Magician of Faith sibling).</summary>
        static readonly Regex RxFlipTrapToHand = new(
            @"FLIP:\s*Target 1 Trap in your GY;\s*add that target to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTrapHole = new(
            @"When your opponent Normal or Flip Summons 1 monster with (\d+) or more ATK:\s*Target that monster;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Bottomless Trap Hole family: opponent Summons (NS/FS/SS) ATK ≥ N,
        /// destroy that monster and banish it (skip GY). Not a target.
        /// </summary>
        static readonly Regex RxSummonDestroyBanish = new(
            @"When your opponent Summons a monster\(s\) with (\d+) or more ATK:\s*" +
            @"Destroy that monster\(s\) with \1 or more ATK, and if you do, banish it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Adhesion Trap Hole: opponent Summons (any) → halve original ATK.</summary>
        static readonly Regex RxAdhesionTrapHole = new(
            @"When your opponent Summons a monster\(s\):\s*Halve that monster\(s\)['’]s original ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Torrential Tribute: a monster is Summoned (either player, including Set/SS) → destroy all.</summary>
        static readonly Regex RxTorrentialTribute = new(
            @"When a monster\(s\) is Summoned:\s*Destroy all monsters on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Eatgaboon: opponent NS/FS ATK ≤ N destroy (excludes SS).</summary>
        static readonly Regex RxOppNsFsAtkLeqDestroy = new(
            @"If the ATK of a monster summoned by your opponent \(excluding Special Summon\) is (\d+) points or less, the monster is destroyed\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>House of Adhesive Tape: opponent NS/FS DEF ≤ N destroy (excludes SS).</summary>
        static readonly Regex RxOppNsFsDefLeqDestroy = new(
            @"If the DEF of a monster summoned by your opponent \(excluding Special Summon\) is (\d+) points or less, the monster is destroyed\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Token Feastevil: Token SS → destroy tokens, 300 damage each.</summary>
        static readonly Regex RxTokenFeastevil = new(
            @"When a Token\(s\) is Special Summoned:\s*Destroy as many Tokens on the field as possible, and if you do, inflict (\d+) damage to your opponent for each Token destroyed\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mispolymerization: Fusion SS → bounce all face-up Fusions to Extra.</summary>
        static readonly Regex RxMispolymerization = new(
            @"Activate only when a Fusion Monster is Special Summoned\.\s*Return all face-up Fusion Monsters to their respective Extra Decks\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Chain Disappearance: summoned ATK ≤ N is banished, then opponent same-name from hand/Deck.</summary>
        static readonly Regex RxChainDisappearance = new(
            @"When a monster\(s\) with (\d+) or less ATK is Summoned:\s*" +
            @"Banish that monster\(s\) with \1 or less ATK, then your opponent banishes all cards with the same name as that card\(s\) from their hand and Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Chain Destruction: summoned ATK ≤ N, target 1, destroy same name in that controller's hand/Deck.</summary>
        static readonly Regex RxChainDestruction = new(
            @"When a monster\(s\) with (\d+) or less ATK is Summoned:\s*" +
            @"Target 1 of them;\s*destroy all cards with that name in its controller's hand and Main Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Blast Held by a Tribute: Tribute Summoned attacker → wipe opp ATK, 1000 damage.</summary>
        static readonly Regex RxBlastHeldByTribute = new(
            @"When an opponent's monster that was Tribute Summoned declares an attack:\s*" +
            @"Destroy as many face-up Attack Position monsters they control as possible, and if you do, inflict (\d+) damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDisarmament = new(
            @"Destroy all Equip Cards on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEternalRest = new(
            @"Destroy all monsters equipped with Equip Cards\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMirrorForce = new(
            @"When an opponent's monster declares an attack:\s*Destroy all your opponent's Attack Position monsters\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNegateAttack = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate the attack, then end the Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxWaboku = new(
            @"You take no battle damage from your opponent's monsters this turn\.\s*Your monsters cannot be destroyed by battle this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCardDestruction = new(
            @"Both players discard as many cards as possible from their hands, then each player draws the same number of cards they discarded\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSangan = new(
            @"If this card is sent from the field to the GY:\s*Add 1 monster with (\d+) or less (ATK|DEF) from your Deck to your hand",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlute = new(
            @"Special Summon up to (\d+) Dragon monsters? from your hand\.\s*""Lord of D\.?"" must be on the field",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPolymerization = new(
            @"Fusion Summon 1 Fusion Monster from your Extra Deck, using monsters from your hand or field as Fusion Material\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Named Greater (Black Luster Ritual / Contract with the Dark Master):
        /// "This card is used to Ritual Summon "NAME". You must also Tribute monsters
        /// from your hand or field whose total Levels equal N or more."
        /// </summary>
        static readonly Regex RxRitualNamedGreater = new(
            @"This card is used to Ritual Summon ""([^""]+)""\.\s*" +
            @"You must also (?:Tribute|offer) monsters(?: whose total Level(?:s| Stars)? equal (\d+) or more(?: as a Tribute)? from the field or your hand| from your hand or field whose total Levels equal (\d+) or more)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Named Greater, singular (Black Illusion Ritual):
        /// "Tribute a monster from your hand or field whose Level is N or more."
        /// </summary>
        static readonly Regex RxRitualNamedGreaterOne = new(
            @"This card is used to Ritual Summon ""([^""]+)""\.\s*" +
            @"You must also Tribute a monster from your hand or field whose Level is (\d+) or more\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Named Equal: "total Levels exactly equal N".
        /// </summary>
        static readonly Regex RxRitualNamedEqual = new(
            @"This card is used to Ritual Summon ""([^""]+)""\.\s*" +
            @"You must also Tribute monsters from your hand or field whose total Levels exactly equal (\d+)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attribute Equal (Earth Chant / Contract with the Abyss):
        /// "Ritual Summon any ATTR Ritual Monster" + exact Level of that monster.
        /// </summary>
        static readonly Regex RxRitualAttributeEqual = new(
            @"This card can be used to Ritual Summon any (\w+) Ritual Monster\.\s*" +
            @"You must also Tribute monsters from your hand or field whose total Levels exactly equal the Level of the Ritual Monster you Ritual Summon\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxLordOfD = new(
            @"Neither player can target Dragon monsters on the field with card effects\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwords = new(
            @"After this card's activation, it remains on the field, but you must destroy it during the End Phase of your opponent's (\d+)(?:rd|nd|th|st)? turn\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwordsAttack = new(
            @"While this card is face-up on the field, your opponent's monsters cannot declare an attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwordsFlip = new(
            @"When this card is activated:\s*If your opponent controls a face-down monster, flip all monsters they control face-up\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxRing = new(
            @"During your opponent's turn:\s*Target 1 face-up monster your opponent controls whose ATK is less than or equal to their LP;\s*destroy that face-up monster, and if you do, take damage equal to its original ATK, then inflict damage to your opponent, equal to the damage you took",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEnemyControllerPos = new(
            @"Target 1 face-up monster your opponent controls;\s*change that target's battle position",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Attack-declared named-field gate (Gravekeeper's Assailant family).
        /// </summary>
        static readonly Regex RxAttackNamedChangePosition = new(
            @"When this card declares an attack while ""([^""]+)"" is on the field:\s*" +
            @"You can target 1 face-up monster your opponent controls;\s*change that target's battle position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCyberJar = new(
            @"FLIP:\s*Destroy all monsters on the field, then both players reveal the top (\d+) cards from their Decks",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Kuriboh-style hand QE at damage calculation:
        /// "During damage calculation, if your opponent's monster attacks (Quick Effect):
        ///  You can discard this card; you take no battle damage from that battle."
        /// </summary>
        /// <summary>
        /// Name condition (A Legendary Ocean, latest PSCT + prior errata):
        /// "(This card's name is always treated as "Umi".)" /
        /// "(This card is always treated as "Umi".)"
        /// </summary>
        static readonly Regex RxAlwaysTreatedAsName = new(
            @"\(This card(?:'s name)? is always treated as (?:an? )?""([^""]+)""(?: card)?\.?\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNameTreatedAs = new(
            @"This card's name is treated as ""([^""]+)""\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>All WATER monsters on the field gain 200 ATK/DEF (slash form — both stats).</summary>
        static readonly Regex RxAllAttrGainAtkDef = new(
            @"All (\w+) monsters(?: on the field)? gain (\d+) ATK/DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Star Boy / Milus Radiant / Bladefly / Witch's Apprentice:
        /// increase the ATK of all WATER monsters by 500 points and decrease the ATK of all FIRE monsters by 400 points.
        /// </summary>
        static readonly Regex RxIncDecAtk = new(
            @"increase the ATK of all (\w+)(?:-Type)? monsters by (\d+) points and decrease the ATK of all (\w+)(?:-Type)? monsters by (\d+) points\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Hoshiningen / Little Chimera / Harpie Lady 1:
        /// All LIGHT monsters on the field gain 500 ATK, also all DARK monsters on the field lose 400 ATK.
        /// </summary>
        static readonly Regex RxAllGainAtkMaybeLose = new(
            @"All (\w+)(?:-Type)? monsters(?: on the field)? gain (\d+) ATK(?:, also all (\w+)(?:-Type)? monsters(?: on the field)? lose (\d+) ATK)?\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAllLoseAtk = new(
            @"All (\w+)(?:-Type)? monsters(?: on the field)? lose (\d+) ATK\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Command Knight: All Warrior monsters you control gain 400 ATK.</summary>
        static readonly Regex RxAllYouControlGainAtk = new(
            @"All (\w+)(?:-Type)? monsters you control gain (\d+) ATK(?:/?DEF| and DEF)?\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxIncreasesAtkDefOfAll = new(
            @"Increases? the ATK and DEF of all (\w+) monsters(?: on the field)? by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Reduce the Level of all WATER monsters in both players' hands and on the field by 1.
        /// </summary>
        static readonly Regex RxReduceLevelHandsAndField = new(
            @"Reduce the Level of all (\w+) monsters in both players?' hands and on the field by (\d+)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDowngradeLevelHandsAndField = new(
            @"Downgrade all (\w+) monsters in both player'?s?'? hands and on the field by (\d+) Level\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Abyss Soldier (and same-shape ignition):
        /// "Once per turn: You can discard 1 WATER monster to the Graveyard to target 1 card on the field; return it to the hand."
        /// </summary>
        static readonly Regex RxDiscardAttrBounceField = new(
            @"Once per turn:\s*You can discard 1 (\w+) monster to the (?:GY|Graveyard) to target 1 card on the field;\s*return it to the hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDiscardAttrBounceFieldPsct = new(
            @"Once per turn:\s*You can discard 1 (\w+) monster(?: to the (?:GY|Graveyard))?;\s*target 1 card on the field;\s*return (?:it|that target) to the hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxKuribohStyle = new(
            @"During damage calculation, if your opponent's monster attacks(?:\s*\(Quick Effect\))?:\s*" +
            @"You can discard this card;\s*you take no battle damage from that battle\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Amphibious Bugroth MK-3 / Yugipedia: continuous, no Chain Link.
        /// "While "Umi" is face-up on the field, this card can attack your opponent's Life Points directly."
        /// Older: "As long as "Umi" remains face-up on the field, …"
        /// </summary>
        static readonly Regex RxDirectAttackWhileNamed = new(
            @"(?:While|As long as) ""([^""]+)"" (?:is|remains)(?: face-up)? on the field, " +
            @"this card can attack (?:your opponent(?:'s Life Points)? )?directly\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Unconditional direct attackers (Ooguchi, Jinzo #7, Nightmare Horse, …).
        /// Requires "This card/monster can|may" so Toon "Can attack … unless" does not match.
        /// Negative lookahead so "cannot attack directly" (Zombyra) is not a grant.
        /// </summary>
        static readonly Regex RxDirectAttackUnconditional = new(
            @"This (?:card|monster) (?:may|can(?!not)) attack (?:your opponent(?:'s Life Points)? )?directly" +
            @"(?: even if there is a monster on your opponent's side of the field)?(?! this turn)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Mermaid Knight: While "Umi" is face-up on the field, this card can attack twice
        /// during the same Battle Phase.
        /// </summary>
        static readonly Regex RxExtraAttackWhileNamed = new(
            @"(?:While|As long as) ""([^""]+)"" (?:is|remains)(?: face-up)? on the field, " +
            @"this card can attack twice during the same Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxExtraAttackUnconditional = new(
            @"This card can attack twice during the same Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSakuretsu = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Fissure: destroy 1 face-up opp monster with the lowest ATK (ties: choose).</summary>
        static readonly Regex RxDestroyLowestAtkOpp = new(
            @"Destroy the 1 face-up monster your opponent controls that has the lowest ATK" +
            @"(?:\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Smashing Ground: destroy 1 face-up opp monster with the highest DEF (ties: choose).</summary>
        static readonly Regex RxDestroyHighestDefOpp = new(
            @"Destroy the 1 face-up monster your opponent controls that has the highest DEF" +
            @"(?:\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Hammer Shot: destroy 1 face-up Attack Position monster with the highest ATK.</summary>
        static readonly Regex RxDestroyHighestAtkFaceUpAtk = new(
            @"Destroy 1 face-up Attack Position monster with the highest ATK" +
            @"(?:\.\s*\(If it is a tie, you get to choose\.\)|\s*\(your choice, if tied\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Castle Walls (legacy): Increase the DEF of 1 face-up monster … until EOT.</summary>
        static readonly Regex RxIncreaseDefUntilEot = new(
            @"Increase the DEF of 1 face-up monster(?: on the field)? by (\d+) points? until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Stop Defense (legacy Select wording).</summary>
        static readonly Regex RxStopDefense = new(
            @"Select 1 Defense Position monster(?: on your opponent's side of the field| your opponent controls)?;?\s*" +
            @"and change it to Attack Position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Enchanted Javelin (legacy): Select attacking monster → gain LP = ATK.</summary>
        static readonly Regex RxEnchantedJavelin = new(
            @"Select 1 attacking monster\.?\s*Gain Life Points equal to its ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Elegant Egotist: If "X" is on the field: SS 1 "X" or "Y" from hand or Deck.</summary>
        static readonly Regex RxElegantEgotist = new(
            @"If ""([^""]+)"" is(?: face-up)? on the field:\s*" +
            @"Special Summon 1 ""([^""]+)"" or ""([^""]+)"" from your hand or Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Umi family: All Fish, Sea Serpent, Thunder, and Aqua … gain N ATK/DEF,
        /// also all Machine and Pyro … lose N ATK/DEF.
        /// </summary>
        static readonly Regex RxMultiRaceGainLoseAtkDef = new(
            @"All (.+?) monsters on the field gain (\d+) ATK/DEF,\s*" +
            @"also all (.+?) monsters on the field lose (\d+) ATK/DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Necrovalley family: All "Gravekeeper's" monsters (on the field) gain N ATK/DEF
        /// or "ATK and DEF". Quoted series, not a race.
        /// </summary>
        static readonly Regex RxQuotedSeriesGainAtkDef = new(
            @"All ""([^""]+)"" monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Harpies' Hunting Ground / Mountain-shape single or multi-word race:
        /// All Winged Beast monsters gain 200 ATK/DEF.
        /// </summary>
        static readonly Regex RxRaceGainAtkDef = new(
            @"All ([A-Za-z]+(?:[ -][A-Za-z]+)*)(?:-Type)? monsters(?: on the field)? gain (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Harpies' Hunting Ground: If any "Harpie Lady" or "Harpie Lady Sisters" is
        /// Normal or Special Summoned: target 1 S/T; destroy it.
        /// </summary>
        static readonly Regex RxNamedOrNamedSummonDestroySt = new(
            @"If any ""([^""]+)"" or ""([^""]+)"" is Normal or Special Summoned:\s*" +
            @"(?:The player who conducted (?:the|that) Summon targets 1 Spell/?Trap on the field;\s*" +
            @"that player destroys that target|" +
            @"Target 1 Spell/?Trap on the field;\s*destroy (?:that target|it))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Rite of Spirit: This card's activation and effect are unaffected by "Necrovalley".</summary>
        static readonly Regex RxUnaffectedByNamed = new(
            @"(?:This card'?s )?activation and effect are unaffected by ""([^""]+)""\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMagicCylinder = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate the attack, and if you do, inflict damage to your opponent equal to its ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDrainingShield = new(
            @"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate that attack, and if you do, gain LP equal to that target's ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDarkMirrorForce = new(
            @"When an opponent's monster declares an attack:\s*Banish all Defense Position monsters your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBarkOfDarkRuler = new(
            @"If a (\w+)(?:-Type)? monster you control battles, during the Damage Step:\s*" +
            @"Pay LP \(in multiples of (\d+) points\), then target the opponent's battling monster;\s*" +
            @"that opponent's monster loses that much ATK and DEF, until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxMaskOfWeakness = new(
            @"Target 1 attacking monster;\s*that target loses (\d+) ATK until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Maiden of the Aqua: the field is treated as "Umi" while face-up (no Umi ATK/DEF).
        /// </summary>
        static readonly Regex RxFieldTreatedAs = new(
            @"(?:As long as this card remains face-up on the field|While this card is face-up on the field), " +
            @"the field is treated as ""([^""]+)""" +
            @"(?: \(however there is no increasing or decreasing of ATK/DEF due to ""[^""]+""'s effect\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNoFieldSpellBlocks = new(
            @"If there is an active Field Spell Card on the field, this effect is not applied\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxActivateOnlyWhileNamed = new(
            @"Activate only (?:while|if) ""([^""]+)"" is(?: face-up)? on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNoBattleDamageWhileNamed = new(
            @"While ""([^""]+)"" is(?: face-up)? on the field, you take no Battle Damage(?: from attacking monsters)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyWhenNamedLeaves = new(
            @"(?:Destroy this card when ""([^""]+)"" leaves the field|If ""([^""]+)"" is not on the field, destroy this card)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Falling Down: Destroy this card unless you control an "Archfiend" card.</summary>
        static readonly Regex RxDestroyUnlessYouControlNamed = new(
            @"Destroy this card unless you control an? ""([^""]+)"" card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSummonGainLp = new(
            @"When this (?:monster|card) is Normal Summoned, Flip Summoned or Special Summoned, " +
            @"increase your Life Points by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Rain of Mercy / PSCT siblings: both players gain the same LP amount.</summary>
        static readonly Regex RxBothPlayersGainLp = new(
            @"(?:(?:increase|increases) the Life Points of both players by (\d+) points?|" +
            @"both players gain (\d+) (?:LP|Life Points))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        static readonly Regex RxConvertOpponentLpGainToDamage = new(
            @"As long as this card remains face-up on the field, any effect of increasing your opponent's Life Points " +
            @"is changed to inflict the same amount of points in damage to your opponent's Life Points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyedToGyDamage = new(
            @"When this card is destroyed and sent to the Graveyard, you take (\d+) points of damage\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Levia-Dragon - Daedalus / same PSCT family:
        /// You can send 1 face-up "Umi" you control to the GY; destroy all other cards on the field.
        /// Cost is a named card you control (A Legendary Ocean is always treated as Umi).
        /// Maiden of the Aqua is environment, not a named Umi, so she is not a legal cost.
        /// </summary>
        static readonly Regex RxSendNamedDestroyAllOther = new(
            @"(?:You can )?send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard);\s*" +
            @"destroy all other cards on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSendNamedDestroyExceptThis = new(
            @"(?:You can )?send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard);\s*" +
            @"destroy all cards on the field except this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxStructuralSummonText = new(@"cannot be normal summoned(?:/set)?|must (?:first )?be special summoned|must be fusion summoned|can only be special summoned", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxRitualProcedureText = new(@"You can Ritual Summon this card with ""[^""]+""\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxRelinquishedBattleSubstitute = new(@"If this card would be destroyed by battle, destroy that equipped monster instead\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxRelinquishedDamageReflect = new(@"While equipped with that monster, any battle damage you take from battles involving this card inflicts equal effect damage to your opponent\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>Compile official text into a program (does not touch cache).</summary>
        public static CompiledCardProgram Compile(CardDef def)
        {
            var prog = new CompiledCardProgram
            {
                CardId = def?.id ?? 0,
                CardName = def?.name ?? "",
                TextHash = OfficialCardAuthority.TextHash(def),
                SourceText = OfficialCardAuthority.OfficialText(def),
                CompiledUtc = DateTime.UtcNow.ToString("o"),
                CompilerVersion = Version,
                CompileSource = "regex"
            };

            if (def == null)
                return prog;

            var text = Normalize(prog.SourceText ?? "");
            var matchedSpans = new List<(int start, int length)>();
            var clauses = new List<EffectClause>();
            var unparsed = new List<string>();

            void Take(Match m, EffectClause clause)
            {
                if (m == null || !m.Success) return;
                clause.SourceSnippet = m.Value.Trim();
                clauses.Add(clause);
                matchedSpans.Add((m.Index, m.Length));
            }

            // Order: multi-sentence templates first, then short ones
            {
                if (def.IsMonster) {
                var neg = SharedPsctAtomTemplates.MatchNegateActivation(text);
                if (neg.Success)
                {
                    var hasDestroy = Regex.IsMatch(neg.Value, "destroy it", RegexOptions.IgnoreCase);
                    var hasDiscard = Regex.IsMatch(neg.Value, "discard 1 card", RegexOptions.IgnoreCase);
                    Take(neg, new EffectClause
                    {
                        Timing = EffectTiming.ChainLinkActivated,
                        Action = EffectActionKind.NegateActivation,
                        ChainResponseOnly = true,
                        DestroyNegatedCard = hasDestroy,
                        FlipSelfFaceUpDefense = Regex.IsMatch(neg.Value, "change this card to face-up defense position", RegexOptions.IgnoreCase),
                        RequiresDiscardCost = hasDiscard,
                        DiscardCostAttribute = hasDiscard ? "*" : null,
                        DiscardCostCount = hasDiscard ? 1 : 0
                    });
                }
                }
            }
            {
                var m = RxCyberJar.Match(text);
                if (m.Success)
                    Take(m, new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.CyberJarStyle,
                        Amount = ParseInt(m, 1, 5)
                    });
            }

            Take(RxSwords.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ApplySwordsOfRevealingLight,
                Amount = ParseInt(RxSwords.Match(text), 1, 3),
                StaysOnField = true
            });

            Take(RxConvertOpponentLpGainToDamage.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ConvertOpponentLpGainToDamage,
                StaysOnField = true,
                MakesChainLink = false
            });
            // Swords sub-clauses absorbed into ApplySwordsOfRevealingLight
            MarkAbsorbed(text, RxSwordsAttack, matchedSpans);
            MarkAbsorbed(text, RxSwordsFlip, matchedSpans);

            Take(RxRing.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.EffectDamageBothFromOriginalAtk,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                OpponentTurnOnly = true
            });

            Take(RxWaboku.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ApplyWabokuStyle
            });

            Take(RxKuribohStyle.Match(text), new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.DiscardSelfNoBattleDamageThisBattle,
                Side = EffectSide.Controller
            });

            Take(RxCardDestruction.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.BothPlayersDiscardAndRedraw
            });

            Take(RxDestroyAllOppMonsters.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.FieldMonsters
            });

            // Dark Hole: all monsters — only if not already matched "opponent controls"
            // or a summon-window Torrential sentence that contains the same fragment.
            var darkHole = RxDestroyAllMonsters.Match(text);
            if (darkHole.Success && !ContainsSnippet(clauses, "your opponent controls") &&
                !RxTorrentialTribute.IsMatch(text))
            {
                Take(darkHole, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldMonsters
                });
            }

            Take(RxDestroyAllST.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldSpellTraps
            });

            Take(RxDestroyAllOppST.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.FieldSpellTraps
            });

            Take(RxTargetStDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true
            });

            Take(RxMonsterReborn.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromGy,
                Zone = EffectZoneFilter.EitherGyMonsters,
                RequiresTargetChoice = true
            });

            Take(RxDraw.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Draw,
                Side = EffectSide.Controller,
                Amount = ParseInt(RxDraw.Match(text), 1, 2)
            });

            Take(RxFlipDestroyMonster.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true
            });

            Take(RxFlipDestroyOppMonster.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true
            });

            Take(RxFlipSpellToHand.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.AddFromGyToHand,
                Zone = EffectZoneFilter.ControllerGySpells,
                RequiresTargetChoice = true
            });

            Take(RxFlipTrapToHand.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.AddFromGyToHand,
                Zone = EffectZoneFilter.ControllerGyTraps,
                RequiresTargetChoice = true
            });

            Take(RxTrapHole.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxTrapHole.Match(text), 1, 1000),
                RequiresTargetChoice = true
            });

            Take(RxSummonDestroyBanish.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxSummonDestroyBanish.Match(text), 1, 1500),
                RequiresTargetChoice = false,
                BanishIfDestroyed = true,
                AnswersSpecialSummon = true
            });

            Take(RxAdhesionTrapHole.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.HalveOriginalAtk,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = false,
                AnswersSpecialSummon = true
            });

            Take(RxTorrentialTribute.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldMonsters,
                RequiresTargetChoice = false,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            var hhg = RxNamedOrNamedSummonDestroySt.Match(text);
            Take(hhg, hhg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.OpponentNormalOrFlipSummon,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    AnswersSpecialSummon = true,
                    AnswersControllerSummon = true,
                    NamedCard = hhg.Groups[1].Value,
                    AltNamedCard = hhg.Groups[2].Value,
                    NamedCardIsSeries = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            Take(RxOppNsFsAtkLeqDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxOppNsFsAtkLeqDestroy.Match(text), 1, 500),
                AmountIsAtkMax = true,
                RequiresTargetChoice = false
            });

            Take(RxOppNsFsDefLeqDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxOppNsFsDefLeqDestroy.Match(text), 1, 500),
                AmountIsDefMax = true,
                RequiresTargetChoice = false
            });

            Take(RxTokenFeastevil.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.DestroyTokensInflictPer,
                Amount = ParseInt(RxTokenFeastevil.Match(text), 1, 300),
                RequiresSummonedIsToken = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxMispolymerization.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.ReturnAllFaceUpFusionsToExtra,
                RequiresSummonedIsFusion = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxChainDisappearance.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.BanishThenSameNameFromOppHandDeck,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxChainDisappearance.Match(text), 1, 1000),
                AmountIsAtkMax = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxChainDestruction.Match(text), new EffectClause
            {
                Timing = EffectTiming.OpponentNormalOrFlipSummon,
                Action = EffectActionKind.DestroySameNameInControllerHandAndDeck,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = ParseInt(RxChainDestruction.Match(text), 1, 2000),
                AmountIsAtkMax = true,
                RequiresTargetChoice = true,
                AnswersSpecialSummon = true,
                AnswersControllerSummon = true
            });

            Take(RxBlastHeldByTribute.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.DestroyOppAttackThenDamage,
                Zone = EffectZoneFilter.OppAttackPositionMonsters,
                Amount = ParseInt(RxBlastHeldByTribute.Match(text), 1, 1000),
                RequiresAttackerTributeSummoned = true
            });

            Take(RxDisarmament.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.DestroyAllEquips
            });

            Take(RxEternalRest.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.DestroyAllEquippedMonsters
            });

            Take(RxMirrorForce.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppAttackPositionMonsters
            });

            Take(RxNegateAttack.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.NegateAttack,
                Zone = EffectZoneFilter.AttackingMonster
            });
            // end battle phase folded into NegateAttack action

            {
                var sang = RxSangan.Match(text);
                var sangDef = sang.Success &&
                    sang.Groups[2].Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                Take(sang, new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.AddFromDeckToHand,
                    Zone = EffectZoneFilter.DeckMonstersAtkLeq,
                    Amount = ParseInt(sang, 1, 1500),
                    AmountIsAtkMax = sang.Success && !sangDef,
                    AmountIsDefMax = sangDef,
                    RequiresTargetChoice = true
                });
            }

            Take(RxFlute.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromHand,
                Zone = EffectZoneFilter.ControllerHandDragons,
                Amount = ParseInt(RxFlute.Match(text), 1, 2),
                RequiresLordOfDOnField = true
            });

            Take(RxPolymerization.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.FusionSummonRegistered
            });

            {
                var namedG = RxRitualNamedGreater.Match(text);
                if (!namedG.Success) namedG = RxRitualNamedGreaterOne.Match(text);
                Take(namedG, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RitualSummon,
                    NamedCard = namedG.Success ? namedG.Groups[1].Value : null,
                    Amount = namedG.Success ? (ParseInt(namedG, 2, 0) > 0 ? ParseInt(namedG, 2, 0) : ParseInt(namedG, 3, 0)) : 0,
                    RitualExactLevel = false,
                    FromHand = true
                });
            }
            Take(RxRitualNamedEqual.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.RitualSummon,
                NamedCard = Group1(RxRitualNamedEqual.Match(text)),
                Amount = ParseInt(RxRitualNamedEqual.Match(text), 2, 0),
                RitualExactLevel = true,
                FromHand = true
            });
            Take(RxRitualAttributeEqual.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.RitualSummon,
                AttributeFilter = Group1(RxRitualAttributeEqual.Match(text)),
                RitualExactLevel = true,
                FromHand = true
            });

            Take(RxLordOfD.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousCannotTargetDragons
            });

            var attackNamedPos = RxAttackNamedChangePosition.Match(text);
            Take(attackNamedPos, new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.ChangeBattlePosition,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                RequiresFaceUpName = attackNamedPos.Success ? attackNamedPos.Groups[1].Value : null
            });

            if (!attackNamedPos.Success)
            {
                Take(RxEnemyControllerPos.Match(text), new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true
                });
            }

            Take(RxAlwaysTreatedAsName.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.AlwaysTreatedAsName,
                TreatedAsName = Group1(RxAlwaysTreatedAsName.Match(text)),
                StaysOnField = true
            });
            if (!ContainsAction(clauses, EffectActionKind.AlwaysTreatedAsName))
            {
                Take(RxNameTreatedAs.Match(text), new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AlwaysTreatedAsName,
                    TreatedAsName = Group1(RxNameTreatedAs.Match(text)),
                    StaysOnField = true
                });
            }

            Take(RxAllAttrGainAtkDef.Match(text), GainAtkDefClause(RxAllAttrGainAtkDef.Match(text)));
            if (!ContainsAction(clauses, EffectActionKind.ContinuousGainAtkDef))
                Take(RxIncreasesAtkDefOfAll.Match(text), GainAtkDefClause(RxIncreasesAtkDefOfAll.Match(text)));

            Take(RxDiscardAttrBounceField.Match(text),
                DiscardBounceClause(RxDiscardAttrBounceField.Match(text)));
            if (!ContainsAction(clauses, EffectActionKind.ReturnToHand))
            {
                Take(RxDiscardAttrBounceFieldPsct.Match(text),
                    DiscardBounceClause(RxDiscardAttrBounceFieldPsct.Match(text)));
            }

            Take(RxReduceLevelHandsAndField.Match(text),
                ReduceLevelClause(RxReduceLevelHandsAndField.Match(text)));
            if (!ContainsAction(clauses, EffectActionKind.ContinuousReduceLevel))
            {
                Take(RxDowngradeLevelHandsAndField.Match(text),
                    ReduceLevelClause(RxDowngradeLevelHandsAndField.Match(text)));
            }

            Take(RxDirectAttackWhileNamed.Match(text),
                DirectAttackClause(RxDirectAttackWhileNamed.Match(text), named: true));
            if (!ContainsAction(clauses, EffectActionKind.CanAttackDirectly))
            {
                Take(RxDirectAttackUnconditional.Match(text),
                    DirectAttackClause(RxDirectAttackUnconditional.Match(text), named: false));
            }

            Take(RxExtraAttackWhileNamed.Match(text),
                ExtraAttackClause(RxExtraAttackWhileNamed.Match(text), named: true));
            if (!ContainsAction(clauses, EffectActionKind.ExtraAttacks))
            {
                Take(RxExtraAttackUnconditional.Match(text),
                    ExtraAttackClause(RxExtraAttackUnconditional.Match(text), named: false));
            }

            Take(RxSakuretsu.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.AttackingMonster,
                CheckedAt = ConditionCheckedAt.Activation
            });

            Take(RxDestroyLowestAtkOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                PickLowestAtk = true
            });
            Take(RxDestroyHighestDefOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                PickHighestDef = true
            });
            Take(RxDestroyHighestAtkFaceUpAtk.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                PickHighestAtk = true,
                RequiresAttackPosition = true
            });

            var incDef = RxIncreaseDefUntilEot.Match(text);
            Take(incDef, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.GainAtkDefUntilEndOfTurn,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                Amount = 0,
                DefAmount = incDef.Success ? ParseInt(incDef, 1, 500) : 500
            });

            Take(RxStopDefense.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ChangeBattlePosition,
                Zone = EffectZoneFilter.OppDefensePositionMonsters,
                RequiresTargetChoice = true,
                ForceAttackPosition = true
            });

            Take(RxEnchantedJavelin.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.GainLpEqualToAtk,
                Zone = EffectZoneFilter.AttackingMonster,
                CheckedAt = ConditionCheckedAt.Activation
            });

            var ego = RxElegantEgotist.Match(text);
            Take(ego, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonNamed,
                RequiresFaceUpName = ego.Success ? ego.Groups[1].Value : null,
                NamedCard = ego.Success ? ego.Groups[2].Value : null,
                AltNamedCard = ego.Success ? ego.Groups[3].Value : null,
                FromHand = true,
                FromDeck = true
            });

            Take(RxMagicCylinder.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.NegateThisAttack,
                Zone = EffectZoneFilter.AttackingMonster
            });
            if (ContainsAction(clauses, EffectActionKind.NegateThisAttack) &&
                RxMagicCylinder.IsMatch(text))
            {
                clauses.Add(new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.InflictDamageEqualToAtk,
                    Zone = EffectZoneFilter.AttackingMonster,
                    SourceSnippet = "inflict damage equal to its ATK"
                });
            }

            Take(RxDrainingShield.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.NegateThisAttack,
                Zone = EffectZoneFilter.AttackingMonster
            });
            if (RxDrainingShield.IsMatch(text) &&
                !ContainsAction(clauses, EffectActionKind.GainLpEqualToAtk))
            {
                clauses.Add(new EffectClause
                {
                    Timing = EffectTiming.AttackDeclared,
                    Action = EffectActionKind.GainLpEqualToAtk,
                    Zone = EffectZoneFilter.AttackingMonster,
                    SourceSnippet = "gain LP equal to that target's ATK"
                });
            }

            Take(RxDarkMirrorForce.Match(text), new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Banish,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppDefensePositionMonsters
            });

            var bark = RxBarkOfDarkRuler.Match(text);
            Take(bark, new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                Zone = EffectZoneFilter.OpponentBattlingMonster,
                RaceFilter = bark.Success ? bark.Groups[1].Value : "Fiend",
                RequiresLpCostMultiple = bark.Success ? ParseInt(bark, 2, 100) : 100,
                DefAmount = 1,
                CheckedAt = ConditionCheckedAt.Both
            });

            var mask = RxMaskOfWeakness.Match(text);
            Take(mask, new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                Zone = EffectZoneFilter.AttackingMonster,
                Amount = mask.Success ? ParseInt(mask, 1, 700) : 700,
                DefAmount = 0
            });

            var fieldAs = RxFieldTreatedAs.Match(text);
            Take(fieldAs, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.FieldTreatedAsName,
                TreatedAsName = fieldAs.Success ? fieldAs.Groups[1].Value : null,
                MakesChainLink = false
            });
            MarkAbsorbed(text, RxNoFieldSpellBlocks, matchedSpans);

            var actNamed = RxActivateOnlyWhileNamed.Match(text);
            Take(actNamed, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.PreventControllerBattleDamage,
                RequiresFaceUpName = actNamed.Success ? actNamed.Groups[1].Value : null,
                StaysOnField = true,
                MakesChainLink = true
            });
            var noBd = RxNoBattleDamageWhileNamed.Match(text);
            Take(noBd, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.PreventControllerBattleDamage,
                RequiresFaceUpName = noBd.Success ? noBd.Groups[1].Value : null,
                StaysOnField = true,
                MakesChainLink = false
            });
            var leave = RxDestroyWhenNamedLeaves.Match(text);
            Take(leave, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.SelfDestroyUnlessNamedFaceUp,
                RequiresFaceUpName = leave.Success ? (leave.Groups[1].Success ? leave.Groups[1].Value : leave.Groups[2].Value) : null,
                MakesChainLink = false
            });
            var unlessYou = RxDestroyUnlessYouControlNamed.Match(text);
            Take(unlessYou, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.SelfDestroyUnlessNamedFaceUp,
                RequiresFaceUpName = unlessYou.Success ? unlessYou.Groups[1].Value : null,
                RequiresControllerNamedCard = true,
                NamedCardIsSeries = true,
                MakesChainLink = false
            });

            var rec = RxSummonGainLp.Match(text);
            Take(rec, new EffectClause
            {
                Timing = EffectTiming.ThisCardSummoned,
                Action = EffectActionKind.GainLifePoints,
                Amount = rec.Success ? ParseInt(rec, 1, 1000) : 1000,
                Side = EffectSide.Controller,
                MakesChainLink = true
            });

            var bothGain = RxBothPlayersGainLp.Match(text);
            Take(bothGain, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.GainLifePoints,
                Amount = bothGain.Success
                    ? ParseInt(bothGain, 1, ParseInt(bothGain, 2, 1000))
                    : 1000,
                Side = EffectSide.Both,
                MakesChainLink = true
            });
            var gyDmg = RxDestroyedToGyDamage.Match(text);
            Take(gyDmg, new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = gyDmg.Success ? ParseInt(gyDmg, 1, 2000) : 2000,
                Side = EffectSide.Controller,
                RequiresDestroyed = true,
                MakesChainLink = true
            });

            var sendOther = RxSendNamedDestroyAllOther.Match(text);
            if (!sendOther.Success) sendOther = RxSendNamedDestroyExceptThis.Match(text);
            Take(sendOther, SendNamedDestroyOthersClause(sendOther));

            ProtectionTemplates.Collect(text, def, clauses, matchedSpans);
            LegacyTextTemplates.Collect(text, def, clauses, matchedSpans);
            AdvancedEffectTemplates.Collect(text, def, clauses, matchedSpans);
            PhaseTriggerTemplates.Collect(text, def, clauses, matchedSpans);
            ContinuousRestrictionTemplates.Collect(text, def, clauses, matchedSpans);
            MonsterTriggerTemplates.Collect(text, def, clauses, matchedSpans);
            MarkAbsorbed(text, RxRitualProcedureText, matchedSpans);
            if (ContainsAction(clauses, EffectActionKind.EquipTargetToThis)) {
                MarkAbsorbed(text, RxRelinquishedBattleSubstitute, matchedSpans);
                MarkAbsorbed(text, RxRelinquishedDamageReflect, matchedSpans);
            }
            // Official PSCT split (condition : cost/target ; resolution) for sentences
            // the whole-card regex did not absorb. This is how new mechanics get in
            // without a unique full-text template for every card.
            EffectClause pendingCost = null;
            var pendingCostStart = 0;
            var pendingCostLen = 0;
            var structuralOnlyText = false;
            foreach (var sent in PsctGrammar.Parse(text, def))
            {
                if (sent == null || sent.Length < 3) continue;
                if (SpanCovered(matchedSpans, sent.IndexInText, sent.Length)) continue;
                if (sent.IsSummonRestriction || RxStructuralSummonText.IsMatch(sent.Raw ?? string.Empty)) { structuralOnlyText = true; matchedSpans.Add((sent.IndexInText, sent.Length)); continue; }
                var costOnly = TryParseCostOnlySentence(sent);
                if (costOnly != null)
                {
                    pendingCost = costOnly;
                    pendingCostStart = sent.IndexInText;
                    pendingCostLen = sent.Length;
                    continue;
                }

                var auras = CompileAuraClauses(sent.Raw);
                if (auras.Count > 0)
                {
                    foreach (var a in auras)
                    {
                        a.SourceSnippet = sent.Raw.Trim();
                        clauses.Add(a);
                    }

                    matchedSpans.Add((sent.IndexInText, sent.Length));
                    continue;
                }

                var clause = CompilePsctSentence(sent, def);
                if (clause == null) continue;
                if (pendingCost != null)
                {
                    MergeActivationCost(clause, pendingCost);
                    matchedSpans.Add((pendingCostStart, pendingCostLen));
                    pendingCost = null;
                }
                clause.SourceSnippet = sent.Raw.Trim();
                clauses.Add(clause);
                matchedSpans.Add((sent.IndexInText, sent.Length));
            }

            // Fairy Meteor Crush family: Equip Spell whose text only describes the
            // equipped monster. Stamp after all Collect so GY-SS Equips (Premature Burial)
            // are not given a field-host Activate.
            if (def != null && def.IsEquipSpell &&
                !ContainsAction(clauses, EffectActionKind.EquipThisToTarget) &&
                !ContainsAction(clauses, EffectActionKind.SpecialSummonFromGy))
            {
                clauses.Add(new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    StaysOnField = true,
                    MakesChainLink = true,
                    SourceSnippet = "(implicit Equip Spell activate)"
                });
            }

            var una = RxUnaffectedByNamed.Match(text);
            if (una.Success)
            {
                foreach (var cl in clauses)
                {
                    if (cl != null)
                        cl.UnaffectedByNamedCard = una.Groups[1].Value;
                }
                matchedSpans.Add((una.Index, una.Length));
            }

            // Unparsed remainder for audit
            var remaining = MaskMatched(text, matchedSpans);
            foreach (var frag in SplitSentences(remaining))
            {
                if (string.IsNullOrWhiteSpace(frag)) continue;
                // Ignore hard-once-per-turn / you can only activate 1 boilerplate
                if (IsBoilerplate(frag)) continue;
                unparsed.Add(frag.Trim());
            }

            // UI choice is not automatically PSCT targeting: Fissure, Smashing Ground,
            // and Hammer Shot keep RequiresTargetChoice but do not target.
            // Equip Spells always target the equipped monster (even pre-PSCT "gains ATK" text).
            foreach (var c in clauses)
            {
                if (c != null && c.Action == EffectActionKind.EquipThisToTarget)
                    c.IsPsctTarget = true;
                if (c != null &&
                    Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"\btarget(?:s|ed|ing)?\b", RegexOptions.IgnoreCase))
                    c.IsPsctTarget = true;
                if (c != null && Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"\byou can\b", RegexOptions.IgnoreCase))
                    c.IsOptional = true;
                if (c != null && Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"^\s*When\b", RegexOptions.IgnoreCase))
                    c.CheckedAt = ConditionCheckedAt.Activation;
                else if (c != null && Regex.IsMatch(c.SourceSnippet ?? string.Empty, @"^\s*If\b", RegexOptions.IgnoreCase))
                    c.CheckedAt = ConditionCheckedAt.Both;
            }

            prog.SetClauses(clauses);
            prog.SetUnparsed(unparsed);
            prog.FullyCompiled = unparsed.Count == 0 && clauses.Count > 0
                                 || (clauses.Count == 0 && OfficialCardAuthority.HasNoActivatableEffect(def))
                                 || (clauses.Count == 0 && structuralOnlyText && unparsed.Count == 0);

            return prog;
        }

        static string Normalize(string s)
        {
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        static int ParseInt(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }

        static bool SpanCovered(List<(int start, int length)> spans, int start, int length)
        {
            if (spans == null || length <= 0) return false;
            var end = start + length;
            var covered = 0;
            foreach (var (s, n) in spans)
            {
                var a = Math.Max(start, s);
                var b = Math.Min(end, s + n);
                if (b > a) covered += b - a;
            }

            return covered * 2 >= length; // majority of the sentence already matched
        }

        /// <summary>
        /// Compile one PSCT sentence from its condition / activation / resolution parts
        /// (Konami green / red / blue), using fragment templates — not a whole-card regex.
        /// </summary>
        static EffectClause CompilePsctSentence(PsctGrammar.Sentence sent, CardDef def)
        {
            if (sent == null) return null;
            if (sent.IsParenthetical)
            {
                var name = RxAlwaysTreatedAsName.Match(sent.Raw);
                if (!name.Success) name = RxNameTreatedAs.Match(sent.Raw);
                if (name.Success)
                    return Stamp(sent, new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.AlwaysTreatedAsName,
                        TreatedAsName = name.Groups[1].Value,
                        StaysOnField = true,
                        MakesChainLink = false
                    });
                return null;
            }

            if (!sent.MakesChainLink)
            {
                var auras = CompileAuraClauses(sent.Raw);
                if (auras.Count == 1)
                    return Stamp(sent, auras[0]);
                var gain = RxAllAttrGainAtkDef.Match(sent.Raw);
                if (!gain.Success) gain = RxIncreasesAtkDefOfAll.Match(sent.Raw);
                if (gain.Success)
                    return Stamp(sent, GainAtkDefClause(gain));
                var lv = RxReduceLevelHandsAndField.Match(sent.Raw);
                if (!lv.Success) lv = RxDowngradeLevelHandsAndField.Match(sent.Raw);
                if (lv.Success)
                    return Stamp(sent, ReduceLevelClause(lv));
                var dirNamed = RxDirectAttackWhileNamed.Match(sent.Raw);
                if (dirNamed.Success)
                    return Stamp(sent, DirectAttackClause(dirNamed, named: true));
                var dirAny = RxDirectAttackUnconditional.Match(sent.Raw);
                if (dirAny.Success)
                    return Stamp(sent, DirectAttackClause(dirAny, named: false));
                var extraNamed = RxExtraAttackWhileNamed.Match(sent.Raw);
                if (extraNamed.Success)
                    return Stamp(sent, ExtraAttackClause(extraNamed, named: true));
                var extraAny = RxExtraAttackUnconditional.Match(sent.Raw);
                if (extraAny.Success)
                    return Stamp(sent, ExtraAttackClause(extraAny, named: false));
                return null;
            }

            var ign = IgnitionTemplates.TryCompile(sent, def);
            if (ign != null)
                return Stamp(sent, ign);

            var act = sent.Activation ?? "";
            var res = sent.Resolution ?? "";
            var combo = (act + " " + res).Trim();

            var clause = new EffectClause
            {
                Timing = sent.SuggestedTiming,
                MakesChainLink = true
            };

            // ── Costs (red text) ──
            var discAttr = Regex.Match(act,
                @"discard 1 (\w+) monster", RegexOptions.IgnoreCase);
            var discCard = Regex.Match(act,
                @"discard (\d+) cards?(?: from your hand(?: to the (?:GY|Graveyard))?)?", RegexOptions.IgnoreCase);
            var sendNamed = Regex.Match(act,
                @"send 1 face-up ""([^""]+)"" you control to the (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (discAttr.Success)
            {
                clause.RequiresDiscardCost = true;
                clause.DiscardCostAttribute = discAttr.Groups[1].Value;
            }
            else if (discCard.Success)
            {
                clause.RequiresDiscardCost = true;
                clause.DiscardCostAttribute = "*";
                clause.DiscardCostCount = int.TryParse(discCard.Groups[1].Value, out var dn) ? dn : 1;
                if (clause.DiscardCostCount < 1) clause.DiscardCostCount = 1;
            }
            else if (sendNamed.Success)
            {
                clause.RequiresSendNamedToGy = true;
                clause.RequiresFaceUpName = sendNamed.Groups[1].Value;
            }

            var paySrc = string.IsNullOrEmpty(act) ? (sent.Raw ?? res) : act;
            var pay = Regex.Match(paySrc,
                @"(?:Activate (?:this card )?by paying|Pay) (\d+) (?:LP|Life Points)",
                RegexOptions.IgnoreCase);
            if (pay.Success)
                clause.PayLpAmount = int.TryParse(pay.Groups[1].Value, out var lp) ? lp : 0;

            // ── Targeting (red text, or "to target" glued onto the cost) ──
            ParseActivationTarget(act, clause);

            // ── Resolution (blue text) ──
            if (Regex.IsMatch(res,
                    @"return (?:it|that target) to (?:its owner's hand|the owner's hand|the hand)",
                    RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.ReturnToHand;
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.AnyCardOnField;
                    clause.RequiresTargetChoice = true;
                }
            }
            else if (Regex.IsMatch(res, @"negate (?:the|that) attack", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.NegateThisAttack;
                clause.Zone = EffectZoneFilter.AttackingMonster;
            }
            else if (Regex.IsMatch(res, @"destroy all other cards on the field", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(res, @"destroy all cards on the field except this card",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.AllOtherCardsOnField;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
            }
            else if (Regex.IsMatch(res, @"destroy (?:that target|it)\.?", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(combo, @"destroy (?:that target|it)\.?", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
                    clause.RequiresTargetChoice = true;
                }
            }
            else if (Regex.IsMatch(res, @"draw (\d+) cards?", RegexOptions.IgnoreCase) &&
                     !Regex.IsMatch(res, @"instead|but if|then draw|additional card",
                         RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res, @"draw (\d+) cards?", RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.Draw;
                clause.Side = EffectSide.Controller;
                clause.Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 1;
            }
            else if (Regex.IsMatch(res, @"inflict (\d+) (?:points of )?damage to your opponent",
                         RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res, @"inflict (\d+) (?:points of )?damage to your opponent",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.InflictDamageToOpponent;
                clause.Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
            }
            else if (Regex.IsMatch(res,
                         @"(?:it|that target) gains (\d+) ATK until the end of this turn",
                         RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(res,
                         @"(?:it|that target) gains (\d+) DEF until the end of this turn",
                         RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(res,
                         @"(?:it|that target) gains (\d+) ATK(?:/?DEF| and DEF) until the end of this turn",
                         RegexOptions.IgnoreCase))
            {
                var mAtk = Regex.Match(res,
                    @"(?:it|that target) gains (\d+) ATK(?:/?DEF| and DEF)? until the end of this turn",
                    RegexOptions.IgnoreCase);
                var mDefOnly = Regex.Match(res,
                    @"(?:it|that target) gains (\d+) DEF until the end of this turn",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.GainAtkDefUntilEndOfTurn;
                if (mAtk.Success)
                {
                    clause.Amount = int.TryParse(mAtk.Groups[1].Value, out var n) ? n : 0;
                    if (mAtk.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0)
                        clause.DefAmount = clause.Amount;
                }
                else if (mDefOnly.Success)
                {
                    clause.Amount = 0;
                    clause.DefAmount = int.TryParse(mDefOnly.Groups[1].Value, out var n) ? n : 0;
                }
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
                    clause.RequiresTargetChoice = true;
                }
            }
            else if (Regex.IsMatch(res,
                         @"this card gains (\d+) ATK and DEF(?! until)", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res, @"this card gains (\d+) ATK and DEF", RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.ApplyLingeringAtkDef;
                clause.Amount = int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
                clause.DefAmount = clause.Amount;
            }
            else if (Regex.IsMatch(res,
                         @"the monster that destroyed it loses (\d+) ATK and DEF",
                         RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res,
                    @"the monster that destroyed it loses (\d+) ATK and DEF",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.ApplyLingeringAtkDef;
                clause.Amount = -(int.TryParse(m.Groups[1].Value, out var n) ? n : 0);
                clause.DefAmount = clause.Amount;
                clause.ImplicitTargetIsBattleDestroyer = true;
                clause.RequiresTargetChoice = false;
            }
            else if (Regex.IsMatch(res, @"destroy the monster that destroyed this card",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.ImplicitTargetIsBattleDestroyer = true;
                clause.RequiresTargetChoice = false;
            }
            else if (Regex.IsMatch(res, @"special summon (?:it|that target)", RegexOptions.IgnoreCase))
            {
                clause.Action = clause.Zone == EffectZoneFilter.ControllerDeckMonsters
                    ? EffectActionKind.SpecialSummonFromDeck
                    : clause.Zone == EffectZoneFilter.ControllerHandMonsters
                        ? EffectActionKind.SpecialSummonFromHand
                        : EffectActionKind.SpecialSummonFromGy;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    clause.Zone = EffectZoneFilter.EitherGyMonsters;
            }
            else if (Regex.IsMatch(res, @"banish (?:it|that target|them)", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(res, @"remove (?:it|that target) from play", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Banish;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (Regex.IsMatch(res,
                         @"return all monsters your opponent controls to (?:the |their owner's |its owner's )?hand",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.ReturnToHand;
                clause.Side = EffectSide.Opponent;
                clause.Zone = EffectZoneFilter.FieldMonsters;
                clause.RequiresTargetChoice = false;
            }
            else if (SharedPsctAtomTemplates.TryCompileSpecialSummon(res, clause))
            {
            }
            else if (Regex.IsMatch(res,
                         @"special summon 1 ""([^""]+)"" or ""([^""]+)"" from your (hand|deck|graveyard|gy)",
                         RegexOptions.IgnoreCase))
            {
                var altSs = Regex.Match(res,
                    @"special summon 1 ""([^""]+)"" or ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                    @"(?: or (hand|deck|graveyard|gy))?",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.SpecialSummonNamed;
                clause.NamedCard = altSs.Groups[1].Value;
                clause.AltNamedCard = altSs.Groups[2].Value;
                void OriginAlt(string loc)
                {
                    if (string.IsNullOrEmpty(loc)) return;
                    if (string.Equals(loc, "hand", StringComparison.OrdinalIgnoreCase))
                        clause.FromHand = true;
                    else if (string.Equals(loc, "deck", StringComparison.OrdinalIgnoreCase))
                        clause.FromDeck = true;
                    else
                        clause.FromGrave = true;
                }
                OriginAlt(altSs.Groups[3].Value);
                if (altSs.Groups.Count > 4 && altSs.Groups[4].Success)
                    OriginAlt(altSs.Groups[4].Value);
            }
            else if (Regex.IsMatch(res,
                         @"special summon 1 ""([^""]+)"" from your (hand|deck|graveyard|gy)",
                         RegexOptions.IgnoreCase))
            {
                var namedSs = Regex.Match(res,
                    @"special summon 1 ""([^""]+)"" from your (hand|deck|graveyard|gy)" +
                    @"(?: or (hand|deck|graveyard|gy))?(?: or (hand|deck|graveyard|gy))?",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.SpecialSummonNamed;
                clause.NamedCard = namedSs.Groups[1].Value;
                void Origin(string loc)
                {
                    if (string.IsNullOrEmpty(loc)) return;
                    if (string.Equals(loc, "hand", StringComparison.OrdinalIgnoreCase))
                        clause.FromHand = true;
                    else if (string.Equals(loc, "deck", StringComparison.OrdinalIgnoreCase))
                        clause.FromDeck = true;
                    else
                        clause.FromGrave = true;
                }
                Origin(namedSs.Groups[2].Value);
                if (namedSs.Groups.Count > 3 && namedSs.Groups[3].Success)
                    Origin(namedSs.Groups[3].Value);
                if (namedSs.Groups.Count > 4 && namedSs.Groups[4].Success)
                    Origin(namedSs.Groups[4].Value);
            }
            else if (Regex.IsMatch(res,
                         @"special summon 1 (\w+)(?:-Type)? monster from your hand",
                         RegexOptions.IgnoreCase))
            {
                var raceSs = Regex.Match(res,
                    @"special summon 1 (\w+)(?:-Type)? monster from your hand",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.SpecialSummonFromHand;
                clause.RaceFilter = raceSs.Groups[1].Value;
                clause.FromHand = true;
                clause.Amount = 1;
            }
            else if (Regex.IsMatch(res, @"add (?:that target|it) to your hand", RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.AddFromGyToHand;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    return null;
            }
            else if (SharedPsctAtomTemplates.TryCompileChangePosition(res, clause))
            {
            }
                        else if (Regex.IsMatch(res, @"change (?:that target's|its) battle position",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.ChangeBattlePosition;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    clause.Zone = EffectZoneFilter.OppFaceUpMonsters;
            }
            else if (Regex.IsMatch(res,
                         @"change that target to face-down Defense Position",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.SetTargetFaceDownDefense;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
            }
            else if (Regex.IsMatch(res,
                         @"(?:it|that target) gains (\d+) (ATK|DEF) until the end of this turn",
                         RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res,
                    @"(?:it|that target) gains (\d+) (ATK|DEF) until the end of this turn",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.GainAtkDefUntilEndOfTurn;
                var n = int.TryParse(m.Groups[1].Value, out var gStat) ? gStat : 0;
                if (m.Groups[2].Value.Equals("DEF", StringComparison.OrdinalIgnoreCase))
                    clause.DefAmount = n;
                else
                    clause.Amount = n;
                if (clause.Zone == EffectZoneFilter.None)
                {
                    clause.Zone = EffectZoneFilter.FieldAnyMonster;
                    clause.RequiresTargetChoice = true;
                }
            }
            else if (Regex.IsMatch(res,
                         @"destroy all cards on the field(?! except)",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.AllOtherCardsOnField;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
            }
            else if (Regex.IsMatch(res,
                         @"destroy all face-up Continuous (Spell|Trap) Cards",
                         RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(res,
                    @"destroy all face-up Continuous (Spell|Trap) Cards",
                    RegexOptions.IgnoreCase);
                clause.Action = EffectActionKind.Destroy;
                clause.Zone = EffectZoneFilter.FieldSpellTraps;
                clause.Side = EffectSide.Both;
                clause.RequiresTargetChoice = false;
                if (m.Groups[1].Value.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                    clause.FaceUpContinuousSpellsOnly = true;
                else
                    clause.FaceUpContinuousTrapsOnly = true;
            }
            else if (Regex.IsMatch(res,
                         @"change (?:it|that target) to (?:face-up )?Attack Position",
                         RegexOptions.IgnoreCase))
            {
                clause.Action = EffectActionKind.ChangeBattlePosition;
                clause.ForceAttackPosition = true;
                clause.RequiresTargetChoice = true;
                if (clause.Zone == EffectZoneFilter.None)
                    clause.Zone = EffectZoneFilter.OppDefensePositionMonsters;
            }

            if (clause.Action == EffectActionKind.None)
            {
                // Continuous/Field/Equip: paying LP to play the card can be the whole activation.
                if (clause.PayLpAmount <= 0 || def == null ||
                    !(def.IsContinuousSpellOrTrap || def.IsFieldSpell || def.IsEquipSpell))
                    return null;
                clause.StaysOnField = true;
            }
            if (HasUnparsedActivationCost(act, clause))
                return null;

            // Activation condition: If "Name" is (face-up) on the field → RequiresFaceUpName.
            var cond = sent.Condition ?? "";
            if (string.IsNullOrEmpty(clause.RequiresFaceUpName))
            {
                var namedOnField = Regex.Match(cond,
                    @"^(?:If|When) ""([^""]+)"" is(?: face-up)? on the field\.?$",
                    RegexOptions.IgnoreCase);
                if (namedOnField.Success)
                    clause.RequiresFaceUpName = namedOnField.Groups[1].Value;
            }

            Stamp(sent, clause);
            if (IsUnrecognizedTriggerAsActivate(sent, clause))
                return null;
            return clause;
        }

        /// <summary>
        /// "If/When … :" that we did not map to a real trigger window must not become
        /// a free Main Phase ignition (Lord Poison is destroyed-by-battle, not OPT).
        /// </summary>
        static bool IsUnrecognizedTriggerAsActivate(PsctGrammar.Sentence sent, EffectClause clause)
        {
            if (sent == null || clause == null) return false;
            if (clause.Timing != EffectTiming.Activate) return false;
            if (sent.OncePerTurn) return false;
            var cond = (sent.Condition ?? "").Trim();
            if (cond.Length == 0) return false;
            if (cond.IndexOf("this card is activated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cond.IndexOf("this card resolves", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            // Recognized activation gate (Elegant Egotist): If "Name" is on the field.
            if (!string.IsNullOrEmpty(clause.RequiresFaceUpName) &&
                Regex.IsMatch(cond,
                    @"^(?:If|When) ""[^""]+"" is(?: face-up)? on the field\.?$",
                    RegexOptions.IgnoreCase))
                return false;
            return cond.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("During ", StringComparison.OrdinalIgnoreCase) ||
                   cond.StartsWith("At ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// PSCT red-text targeting. GY monster/Spell/Trap must be parsed here —
        /// "add it to your hand" must not guess a zone (Magician of Faith is Spell;
        /// Monster Reincarnation is a monster).
        /// </summary>
        static void ParseActivationTarget(string act, EffectClause clause)
        {
            if (string.IsNullOrEmpty(act) || clause == null) return;
            if (Regex.IsMatch(act, @"target 1 (?:spell/?trap|spell or trap)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.FieldSpellTraps;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 card on the field", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"to target 1 card on the field", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.AnyCardOnField;
                return;
            }

            if (Regex.IsMatch(act, @"target the attacking monster", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act, @"target 1 attacking monster", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = false;
                clause.Zone = EffectZoneFilter.AttackingMonster;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 (?:a )?monster in (?:your|the) Deck", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerDeckMonsters;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 monster in either (?:GY|Graveyard)",
                    RegexOptions.IgnoreCase) ||
                Regex.IsMatch(act,
                    @"select 1 monster card from you or your opponent's graveyard",
                    RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.EitherGyMonsters;
                return;
            }

            var gyNamed = Regex.Match(act,
                @"target 1 ""([^""]+)"" monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyNamed.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                clause.NamedCard = gyNamed.Groups[1].Value;
                clause.NamedCardIsSeries = true;
                return;
            }

            var gyMon = Regex.Match(act,
                @"target 1 (?:(\w+)(?:-Type)? )?monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyMon.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                if (gyMon.Groups[1].Success && gyMon.Groups[1].Length > 0)
                    clause.RaceFilter = gyMon.Groups[1].Value;
                var exceptNamed = Regex.Match(act, @"except ""([^""]+)""", RegexOptions.IgnoreCase);
                if (exceptNamed.Success)
                    clause.ExceptNamedCard = exceptNamed.Groups[1].Value;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 Spell in your (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGySpells;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 Trap in your (?:GY|Graveyard)", RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyTraps;
                return;
            }

            if (Regex.IsMatch(act, @"target 1 (?:face-up )?monster (?:on the field|your opponent controls)",
                    RegexOptions.IgnoreCase))
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = Regex.IsMatch(act, @"opponent", RegexOptions.IgnoreCase)
                    ? EffectZoneFilter.OppFaceUpMonsters
                    : EffectZoneFilter.FieldAnyMonster;
            }
        }

        /// <summary>
        /// Activation text names a cost this compiler did not parse — refuse the
        /// sentence instead of shipping a partial program (Spell Reproduction).
        /// </summary>
        static bool HasUnparsedActivationCost(string act, EffectClause clause)
        {
            if (string.IsNullOrEmpty(act) || clause == null) return false;
            if (Regex.IsMatch(act, @"discard \d+", RegexOptions.IgnoreCase) &&
                !clause.RequiresDiscardCost && !clause.RequiresDiscardSelf)
                return true;
            if (Regex.IsMatch(act, @"send \d+", RegexOptions.IgnoreCase) &&
                !clause.RequiresSendNamedToGy && !clause.RequiresSendThisToGy &&
                !clause.RequiresSendHandToGy && !clause.RequiresSendOtherYouControl)
                return true;
            if (Regex.IsMatch(act, @"pay \d+", RegexOptions.IgnoreCase) &&
                clause.PayLpAmount <= 0 && clause.RequiresLpCostMultiple <= 0)
                return true;
            if (Regex.IsMatch(act, @"tribute (?:this|\d+)", RegexOptions.IgnoreCase) &&
                !clause.RequiresTributeThis && clause.RequiresTributeCount <= 0)
                return true;
            if (Regex.IsMatch(act, @"banish \d+", RegexOptions.IgnoreCase) &&
                clause.BanishFromGyCount <= 0)
                return true;
            if (Regex.IsMatch(act, @"banish the top", RegexOptions.IgnoreCase))
                return true;
            return false;
        }

        static EffectClause TryParseCostOnlySentence(PsctGrammar.Sentence sent)
        {
            if (sent == null) return null;
            var raw = (sent.Raw ?? "").Trim();
            if (raw.Length < 3) return null;
            var disc = Regex.Match(raw,
                @"^Discard (\d+) cards?(?: from your hand(?: to the (?:GY|Graveyard))?)?\s*\.?\s*$",
                RegexOptions.IgnoreCase);
            if (disc.Success)
            {
                var n = int.TryParse(disc.Groups[1].Value, out var dn) ? dn : 1;
                if (n < 1) n = 1;
                return new EffectClause
                {
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = n
                };
            }

            var pay = Regex.Match(raw,
                @"^(?:Activate (?:this card )?by paying|Pay) (\d+) (?:LP|Life Points)\s*\.?\s*$",
                RegexOptions.IgnoreCase);
            if (!pay.Success) return null;
            var lp = int.TryParse(pay.Groups[1].Value, out var pv) ? pv : 0;
            if (lp <= 0) return null;
            return new EffectClause { PayLpAmount = lp };
        }

        static void MergeActivationCost(EffectClause dest, EffectClause cost)
        {
            if (dest == null || cost == null) return;
            if (cost.RequiresDiscardCost)
            {
                dest.RequiresDiscardCost = true;
                if (string.IsNullOrEmpty(dest.DiscardCostAttribute))
                    dest.DiscardCostAttribute = cost.DiscardCostAttribute;
                if (dest.DiscardCostCount < 1)
                    dest.DiscardCostCount = cost.DiscardCostCount > 0 ? cost.DiscardCostCount : 1;
            }

            if (cost.PayLpAmount > 0 && dest.PayLpAmount <= 0)
                dest.PayLpAmount = cost.PayLpAmount;
        }

        static EffectClause Stamp(PsctGrammar.Sentence sent, EffectClause clause)
        {
            if (clause == null) return null;
            clause.OncePerTurn = clause.OncePerTurn || sent.OncePerTurn;
            if (clause.OncePerTurn && clause.OptScope == OncePerTurnScope.None)
                clause.OptScope = OncePerTurnScope.PerInstance;
            clause.CheckedAt = sent.CheckedAt;
            clause.IsQuickEffect = clause.IsQuickEffect || sent.IsQuickEffect;
            clause.IsOptional = clause.IsOptional || sent.IsOptional;
            clause.MakesChainLink = sent.MakesChainLink;
            if (sent.IsFlip)
                clause.Timing = EffectTiming.Flip;
            else if (clause.Timing == EffectTiming.None)
                clause.Timing = sent.SuggestedTiming;
            var hay = (sent.Condition ?? "") + " " + (sent.Raw ?? "");
            if (hay.IndexOf("flip summoned", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresThisFlipSummoned = true;
            if (hay.IndexOf("this card is tribute summoned", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hay.IndexOf("this monster is tribute summoned", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresThisTributeSummoned = true;
            if (hay.IndexOf("normal summoned", StringComparison.OrdinalIgnoreCase) >= 0 &&
                hay.IndexOf("flip summoned", StringComparison.OrdinalIgnoreCase) < 0 &&
                hay.IndexOf("special summoned", StringComparison.OrdinalIgnoreCase) < 0)
                clause.RequiresThisNormalSummoned = true;
            if (hay.IndexOf("by a direct attack", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresDirectAttack = true;
            if (hay.IndexOf("destroyed by battle", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.RequiresThisDestroyedByBattle = true;
            else if (Regex.IsMatch(hay,
                         @"destroyed and sent from the field to the (?:GY|Graveyard)|this card is destroyed and sent",
                         RegexOptions.IgnoreCase))
                clause.RequiresDestroyed = true;
            if (hay.IndexOf("during your opponent's turn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hay.IndexOf("during the opponent's turn", StringComparison.OrdinalIgnoreCase) >= 0)
                clause.OpponentTurnOnly = true;
            return clause;
        }

        static EffectClause DiscardBounceClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ReturnToHand,
                Zone = EffectZoneFilter.AnyCardOnField,
                RequiresTargetChoice = true,
                OncePerTurn = true,
                RequiresDiscardCost = true,
                DiscardCostAttribute = m.Groups[1].Value
            };
        }

        static EffectClause SendNamedDestroyOthersClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.AllOtherCardsOnField,
                Side = EffectSide.Both,
                RequiresTargetChoice = false,
                RequiresSendNamedToGy = true,
                RequiresFaceUpName = m.Groups[1].Value,
                IsOptional = true,
                MakesChainLink = true
            };
        }

        static EffectClause ExtraAttackClause(Match m, bool named)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ExtraAttacks,
                Amount = 1,
                RequiresFaceUpName = named && m.Groups.Count > 1 ? m.Groups[1].Value : null,
                MakesChainLink = false
            };
        }

        static EffectClause DirectAttackClause(Match m, bool named)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CanAttackDirectly,
                RequiresFaceUpName = named && m.Groups.Count > 1 ? m.Groups[1].Value : null,
                MakesChainLink = false
            };
        }

        static EffectClause GainAtkDefClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            var n = ParseInt(m, 2, 0);
            var c = new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Amount = n,
                DefAmount = n,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = false
            };
            FillTypeOrAttribute(c, m.Groups[1].Value);
            return c;
        }

        static readonly HashSet<string> AttributeWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND", "DIVINE"
        };

        static string StripContinuousWhile(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = Normalize(s);
            s = Regex.Replace(s,
                @"^(?:As long as this card remains face-up on the field,\s*|While this card is face-up on the field,\s*)",
                "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        /// <summary>
        /// Continuous ATK/DEF auras that do not use a colon (not a Chain Link).
        /// Star Boy family + modern "All X gain ATK, also all Y lose ATK" + "you control".
        /// Requires the aura to be the whole remaining sentence so Defense-only / Flip prefixes do not match.
        /// </summary>
        static List<EffectClause> CompileAuraClauses(string raw)
        {
            var list = new List<EffectClause>();
            var body = StripContinuousWhile(raw);
            if (string.IsNullOrEmpty(body)) return list;

            var multi = RxMultiRaceGainLoseAtkDef.Match(body);
            if (multi.Success && multi.Index == 0)
            {
                var gainN = ParseInt(multi, 2, 200);
                var loseN = ParseInt(multi, 4, 200);
                foreach (var race in SplitRaceList(multi.Groups[1].Value))
                    list.Add(StatAuraClause(race, gainN, gainN, EffectSide.Both));
                foreach (var race in SplitRaceList(multi.Groups[3].Value))
                    list.Add(StatAuraClause(race, -loseN, -loseN, EffectSide.Both));
                return list;
            }

            var quoted = RxQuotedSeriesGainAtkDef.Match(body);
            if (quoted.Success && quoted.Index == 0)
            {
                var n = ParseInt(quoted, 2, 0);
                var both = quoted.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                var c = StatAuraClause(null, n, both ? n : 0, EffectSide.Both);
                c.NamedCard = quoted.Groups[1].Value;
                c.NamedCardIsSeries = true;
                list.Add(c);
                return list;
            }

            var raceGain = RxRaceGainAtkDef.Match(body);
            if (raceGain.Success && raceGain.Index == 0)
            {
                var n = ParseInt(raceGain, 2, 0);
                var both = raceGain.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(StatAuraClause(raceGain.Groups[1].Value, n, both ? n : 0, EffectSide.Both));
                return list;
            }

            var inc = RxIncDecAtk.Match(body);
            if (inc.Success && inc.Index == 0)
            {
                list.Add(StatAuraClause(inc.Groups[1].Value, ParseInt(inc, 2, 0), 0, EffectSide.Both));
                list.Add(StatAuraClause(inc.Groups[3].Value, -ParseInt(inc, 4, 0), 0, EffectSide.Both));
                return list;
            }

            var gain = RxAllGainAtkMaybeLose.Match(body);
            if (gain.Success && gain.Index == 0)
            {
                list.Add(StatAuraClause(gain.Groups[1].Value, ParseInt(gain, 2, 0), 0, EffectSide.Both));
                if (gain.Groups.Count > 3 && gain.Groups[3].Success &&
                    !string.IsNullOrEmpty(gain.Groups[3].Value))
                    list.Add(StatAuraClause(gain.Groups[3].Value, -ParseInt(gain, 4, 0), 0,
                        EffectSide.Both));
                return list;
            }

            var lose = RxAllLoseAtk.Match(body);
            if (lose.Success && lose.Index == 0)
            {
                list.Add(StatAuraClause(lose.Groups[1].Value, -ParseInt(lose, 2, 0), 0, EffectSide.Both));
                return list;
            }

            var you = RxAllYouControlGainAtk.Match(body);
            if (you.Success && you.Index == 0)
            {
                var n = ParseInt(you, 2, 0);
                var bothStats = you.Value.IndexOf("DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                list.Add(StatAuraClause(you.Groups[1].Value, n, bothStats ? n : 0, EffectSide.Controller));
                return list;
            }

            return list;
        }

        static EffectClause StatAuraClause(string filter, int atk, int def, EffectSide side)
        {
            var c = new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Amount = atk,
                DefAmount = def,
                Side = side,
                StaysOnField = true,
                MakesChainLink = false
            };
            FillTypeOrAttribute(c, filter);
            return c;
        }

        /// <summary>
        /// "Fish, Sea Serpent, Thunder, and Aqua" → individual race tokens.
        /// Keeps multi-word races (Sea Serpent) intact.
        /// </summary>
        static List<string> SplitRaceList(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            var s = raw.Replace(", and ", ", ").Replace(" and ", ", ");
            foreach (var part in s.Split(','))
            {
                var race = part.Trim();
                if (race.Length > 0) list.Add(race);
            }
            return list;
        }

        static void FillTypeOrAttribute(EffectClause c, string word)
        {
            if (c == null || string.IsNullOrEmpty(word)) return;
            if (AttributeWords.Contains(word))
                c.AttributeFilter = word;
            else
                c.RaceFilter = word;
        }

        static EffectClause ReduceLevelClause(Match m)
        {
            if (m == null || !m.Success) return new EffectClause();
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousReduceLevel,
                AttributeFilter = m.Groups[1].Value,
                Amount = ParseInt(m, 2, 1),
                ApplyToHand = true,
                ApplyToField = true,
                StaysOnField = true
            };
        }

        static string Group1(Match m) =>
            m != null && m.Success && m.Groups.Count > 1 ? m.Groups[1].Value : "";

        static bool ContainsAction(List<EffectClause> clauses, EffectActionKind action)
        {
            foreach (var c in clauses)
                if (c != null && c.Action == action) return true;
            return false;
        }

        static bool ContainsSnippet(List<EffectClause> clauses, string needle)
        {
            foreach (var c in clauses)
                if (c.SourceSnippet != null &&
                    c.SourceSnippet.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static void MarkAbsorbed(string text, Regex rx, List<(int, int)> spans)
        {
            var m = rx.Match(text);
            if (m.Success) spans.Add((m.Index, m.Length));
        }

        static string MaskMatched(string text, List<(int start, int length)> spans)
        {
            if (spans.Count == 0) return text;
            var chars = text.ToCharArray();
            foreach (var (start, length) in spans)
            {
                for (var i = start; i < start + length && i < chars.Length; i++)
                    chars[i] = ' ';
            }

            return new string(chars);
        }

        static IEnumerable<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (var part in Regex.Split(text, @"(?<=[\.\!\?])\s+"))
            {
                var t = part.Trim();
                if (t.Length > 2) yield return t;
            }
        }

        static bool IsBoilerplate(string frag)
        {
            // Collapse mask holes before length checks only — do not invent away real riders/costs.
            var f = Regex.Replace(frag ?? "", @"\s+", " ").Trim().ToLowerInvariant();
            if (f.Contains("you can only activate 1")) return true;
            if (f.Contains("must be face-up on the field to activate and to resolve this effect")) return true;
            if (f.Contains("must control this face-up card to activate and to resolve this effect")) return true;
            if (f.Contains("cannot normal summon/set the turn you special summon this card")) return true;
            if (f.StartsWith("once per turn") && f.Length < 24) return true;
            if (f.Contains("gains 500 atk")) return true;
            if (f.StartsWith("if this card is attacked, change it to attack position")) return true;
            if (f.Contains("you can only use")) return true;
            // Strip the name-condition parenthetical only. Do not eat a glued GY/trigger rider
            // (Axe of Despair: always-treated + "When this card is sent to the GY…").
            if (f.Contains("this card is always treated as"))
            {
                f = Regex.Replace(f,
                    @"\(this card(?:'s name)? is always treated as [^)]+\)\.?",
                    " ").Trim();
                f = Regex.Replace(f, @"\s+", " ").Trim();
                if (f.Length < 8) return true;
            }
            if (f.StartsWith("●")) return true; // multi-choice bullets partially handled
            if (f.Contains("tribute 1 monster, then target")) return true; // EC mode 2 deferred
            if (f.Contains("cannot activate cards, or the effects")) return true; // Sangan restriction
            if (f.Contains("once while this card is face-up")) return true;
            if (f.Contains("union monster at a time")) return true;
            if (f.Contains("destroy this card instead")) return true;
            if (f.Contains("when that monster is destroyed, destroy this card")) return true;
            if (f.Contains("when this card leaves the field, destroy that monster")) return true;
            // Rite of Spirit / similar: Necrovalley interaction reminder, not a separate effect.
            if (f.Contains("unaffected by \"necrovalley\"")) return true;
            if (Regex.IsMatch(f, @"^""[^""]+""\s*\+\s*""[^""]+""")) return true;
            if (f.Length < 8) return true;
            return false;
        }
    }
}
