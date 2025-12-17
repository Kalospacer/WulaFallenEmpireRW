import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def _indent(elem: ET.Element) -> None:
	# Python 3.9+: keep output stable and readable.
	try:
		ET.indent(elem, space="  ")  # type: ignore[attr-defined]
	except Exception:
		pass


_HAN_RE = re.compile(r"[\u4e00-\u9fff]")


def _assert_no_han(text: str, key: str) -> None:
	if _HAN_RE.search(text or ""):
		raise ValueError(f"English output still contains Han characters for key: {key}")


def _li(text: str) -> list[str]:
	return [text]


def build_translations() -> dict[str, str | list[str]]:
	# NOTE: This is intentionally keyed by the DefInjected keys from the in-game export.
	# Keep placeholders (e.g. {SUBJECT_definite}, [asker_nameDef]) intact.
	return {
		# --- WULA_Base_Tex_Quest ---
		"WULA_Base_Tex_Quest.questDescriptionRules.rulesStrings": _li(
			"questDescription->Only death and taxes are inevitable—submitting tithes on time is the glorious duty of a Wula Empire colony.\n\n"
			"The Wula Empire collects tithes every 10 days, deducted from the colony's assets stored with the fleet. You can construct <color=#6BB7B7><i>Wula Empire Material Transfer Pods</i></color> to transport materials to the fleet in orbit.\n\n"
			"The Wula Empire shows greater favor to colonies that pay taxes diligently—however, delays will cause displeasure each day, and eventually may even be classified as treason!"
		),
		"WULA_Base_Tex_Quest.questNameRules.rulesStrings": _li("questName->Tithe Taxation"),
		"WULA_Base_Tex_Quest.root.nodes.Greater-0.node.value2.slateRef": "0.8",
		"WULA_Base_Tex_Quest.root.nodes.Greater-1.node.value2.slateRef": "0.8",
		"WULA_Base_Tex_Quest.root.nodes.Greater-2.node.value2.slateRef": "0.8",
		"WULA_Base_Tex_Quest.root.nodes.Handle_Outtime.node.nodes.Letter.label.slateRef": "Overdue Tithe",
		"WULA_Base_Tex_Quest.root.nodes.Handle_Outtime.node.nodes.Letter.text.slateRef": "As a colony of the Wula Empire, you have been found delinquent in paying your tithe—maybe once or twice they can forgive you, but continued arrears will surely anger them!",
		"WULA_Base_Tex_Quest.root.nodes.Letter.label.slateRef": "Tithe",
		"WULA_Base_Tex_Quest.root.nodes.Letter.text.slateRef": "Only death and taxes are inevitable—submitting tithes on time is the glorious duty of a Wula Empire colony.\n\nOpen the quest list for details.",
		"WULA_Base_Tex_Quest.root.nodes.TaxPaymentSuccess.node.nodes.Letter.label.slateRef": "Tithe Paid",
		"WULA_Base_Tex_Quest.root.nodes.TaxPaymentSuccess.node.nodes.Letter.text.slateRef": "The Empire has received the payment. In recognition of your diligent taxation, a proof-of-payment certificate has been delivered to your colony!",

		# --- WULA_Boss_Sky_Lock ---
		"WULA_Boss_Sky_Lock.questDescriptionRules.rulesStrings": _li(
			"questDescription->The central control AI of the Wula Empire Planetary Interdiction Agency has sent a special request to the colony. A Wula Empire war machine deployed on a transport ship—<color=#AA74E5><i>Psychic Titan</i></color>—has gone out of control and is attacking indiscriminately. The Agency had no choice but to crash the transport ship onto the planet's surface to prevent the situation from escalating.\n\n"
			"Unfortunately, the <color=#AA74E5><i>Psychic Titan</i></color> was not harmed by the crash and is still slaughtering across the surface. The colony must face this terrifying behemoth. The Agency has provided the following intelligence:\n\n"
			"<color=#9F0400><i>-Closed Circuit</i></color> PAt-6 \"Psychic Titan\" is a powerful psychic war machine, but its psychic circuitry is engraved as a closed loop; external psychic attacks cannot affect it.\n"
			"<color=#9F0400><i>-Harbinger of Death</i></color> PAt-6 \"Psychic Titan\" can unleash a psychic shriek that shatters all targets indiscriminately. Do not engage it head-on unless you are confident you can control it.\n"
			"<color=#9F0400><i>-Chain Skylocks</i></color> PAt-6 \"Psychic Titan\" will always appear with Skylocks. Skylocks are pure psychic constructs that absorb damage taken by the Titan. Until all Skylocks are destroyed, the Titan will not take damage.\n"
			"<color=#9F0400><i>-Network Control</i></color> PAt-6 \"Psychic Titan\" has seized control of nearby chariot clusters that survived the crash, so the crash area likely contains more than one enemy.\n\n"
			"The Agency promises that once the rampaging <color=#AA74E5><i>Psychic Titan</i></color> is defeated, they will recover the core of its psychic circuitry and use it to build a brand-new Psychic Titan for the colony."
		),
		"WULA_Boss_Sky_Lock.questNameRules.rulesStrings": _li("questName->Special Task: Destroy the Psychic Titan"),
		"WULA_Boss_Sky_Lock.root.nodes.Letter.label.slateRef": "Psychic Titan Arrives",
		"WULA_Boss_Sky_Lock.root.nodes.Letter.text.slateRef": "After a cruiser rammed the runaway transport ship, the shattered hull fell like a meteor onto the rimworld's surface. Soon after, the Psychic Titan was sighted on the ground—the location has been marked.",
		"WULA_Boss_Sky_Lock.root.nodes.PsiTitan0Destroyed.node.nodes.Letter.label.slateRef": "Psychic Titan Disabled",
		"WULA_Boss_Sky_Lock.root.nodes.PsiTitan0Destroyed.node.nodes.Letter.text.slateRef": "The Psychic Titan has ceased functioning under the colonists' assault. Its Psychic Circuit Core has been exposed and dropped.\n\nRemember to recover the Psychic Circuit Core—it's required to build a new Psychic Titan.",

		# --- WULA_Boss_Super_Fortress ---
		"WULA_Boss_Super_Fortress.questDescriptionRules.rulesStrings": _li(
			"questDescription->The central control AI of the Wula Empire Planetary Interdiction Agency has sent a special request to the colony. A <color=#C87451><i>Super Fortress</i></color> equipped with volcano cannons has been discovered. It is not under the Agency's control, and is likely a rebel stronghold. Your colony must use all available forces to destroy it. The Agency has provided the following intelligence:\n\n"
			"<color=#9F0400><i>-Preemptive Strike</i></color> The fortress has already been hit by EMP shells from the Wula Empire fleet and will need time to reboot its defenses—but judging from the bombardment results, this window may not last long.\n"
			"<color=#9F0400><i>-Inferno Horn</i></color> There are four volcano cannons inside. Once they come back online, they will deal devastating damage to any assault force and should be neutralized first.\n"
			"<color=#9F0400><i>-Bristling Bastions</i></color> The fortress is protected by multiple layers of defenses. A frontal assault will be very time-consuming, and the top of the fortress has been specially thickened, comparable to a mountain roof.\n"
			"<color=#9F0400><i>-Hidden Ambushes</i></color> There are likely unseen ambushes near the fortress; the fleet's detected activity does not match what can be seen.\n\n"
			"The Agency promises that once the <color=#C87451><i>Super Fortress</i></color> is destroyed, they will send the fleet to bombard the remaining facilities and unlock authorization for the colony to apply for volcano cannons."
		),
		"WULA_Boss_Super_Fortress.questNameRules.rulesStrings": _li("questName->Special Task: Purge the Super Fortress"),
		"WULA_Boss_Super_Fortress.root.nodes.Letter.label.slateRef": "Super Fortress Marked",
		"WULA_Boss_Super_Fortress.root.nodes.Letter.text.slateRef": "The super fortress built by Wula Empire progressives has been marked on the map. Interference prevents the fleet from striking it directly, but if a spotter is nearby, the fleet can still enter orbit above the fortress normally.",

		# --- WULA_Recycle_PIA_Legion_File ---
		"WULA_Recycle_PIA_Legion_File.questDescriptionRules.rulesStrings": _li(
			"questDescription->The central control AI of the Wula Empire Planetary Interdiction Agency has sent a special request to the colony. A lockbox containing imperial secrets has been seized by Wula Empire progressive rebels. The colony must recover it and return it to the Wula Empire fleet. If the lockbox has already been opened, eliminate everyone on site.\n\n"
			"The rebels are likely attempting to open it. You will need to check the workbench inside their outpost. About <color=#AA3020><i>10–20 rebel synths</i></color> have been sighted, and <color=#AA3020><i>several active turrets</i></color> are also present.\n\n"
			"You have only 5 days to handle this task. After obtaining the lockbox, you can construct <color=#6BB7B7><i>Wula Empire Material Transfer Pods</i></color> to send it to the fleet in orbit."
		),
		"WULA_Recycle_PIA_Legion_File.questNameRules.rulesStrings": _li("questName->Promotion Task: Recover Imperial Secrets"),
		"WULA_Recycle_PIA_Legion_File.root.nodes.Handle_Outtime.node.nodes.Letter.label.slateRef": "Lockbox Missing",
		"WULA_Recycle_PIA_Legion_File.root.nodes.Handle_Outtime.node.nodes.Letter.text.slateRef": "The lockbox your superior assigned you to recover has lost its signal. The fleet will need some time to reacquire it.",

		# --- WULA_Vacation_Quest ---
		"WULA_Vacation_Quest.questDescriptionRules.rulesStrings": _li(
			"questDescription->The central control AI of the Planetary Interdiction Agency has been cooped up on the fleet for too long and wants to come out for a while.\n\n"
			"Unless you are hostile to the Wula Empire Planetary Interdiction Agency, it looks like your \"superior\" will keep hanging around the colony. You can delete this quest to keep it from occupying your quest list."
		),
		"WULA_Vacation_Quest.questNameRules.rulesStrings": _li("questName->Special Task: Vacation"),
		"WULA_Vacation_Quest.root.nodes.askerDestroyed.node.nodes.Letter.label.slateRef": "Guest {SUBJECT_definite} Has Died",
		"WULA_Vacation_Quest.root.nodes.askerDestroyed.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has died. [failLetterEndingCommon]",
		"WULA_Vacation_Quest.root.nodes.askerLeftMap.node.nodes.Letter.label.slateRef": "Guest Missing: {SUBJECT_definite}",
		"WULA_Vacation_Quest.root.nodes.askerLeftMap.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has gone missing in your colony. [failLetterEndingCommon]",
		"WULA_Vacation_Quest.root.nodes.askerRanWild.node.nodes.Letter.label.slateRef": "Guest Missing: {SUBJECT_definite}",
		"WULA_Vacation_Quest.root.nodes.askerRanWild.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has returned to the wild in your colony. [failLetterEndingCommon]",
		"WULA_Vacation_Quest.root.nodes.Letter.label.slateRef": "Quest Failed: [resolvedQuestName]",
		"WULA_Vacation_Quest.root.nodes.Letter.text.slateRef": "[faction_name] has become hostile to you.",
		"WULA_Vacation_Quest.root.nodes.lodgersArrested.node.nodes.Letter.label.slateRef": "Guest {SUBJECT_definite} Captured",
		"WULA_Vacation_Quest.root.nodes.lodgersArrested.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has been captured. [failLetterEndingCommon]",
		"WULA_Vacation_Quest.root.nodes.LoopCount.node.nodes.Delay.node.nodes.Letter.label.slateRef": "Tax Receipt?",
		"WULA_Vacation_Quest.root.nodes.LoopCount.node.nodes.Delay.node.nodes.Letter.text.slateRef": "Despite there being no actual tax payment, the colony has received a tax receipt.\n\nLooks like someone is quietly thanking the colony.",
		"WULA_Vacation_Quest.root.nodes.Sequence.nodes.Sequence.nodes.dropoffShipThingDestroyed.node.nodes.Letter.label.slateRef": "Shuttle Destroyed",
		"WULA_Vacation_Quest.root.nodes.Sequence.nodes.Sequence.nodes.dropoffShipThingDestroyed.node.nodes.Letter.text.slateRef": "The shuttle assigned to transport [asker_nameDef] has been destroyed.",

		# --- WULA_Progressive_Ship_Attack_Quest ---
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.Letter.label.slateRef": "Excavator Airstrike",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.Letter.text.slateRef": "Wula Empire progressive rebels have launched an excavator swarm at the colony!",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.ResolveQuestDescription.rules.slateRef": "<rulesStrings><li>questDescription-&gt;Wula Empire progressive rebels have launched an excavator swarm at the colony!</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.ResolveQuestName.rules.slateRef": "<rulesStrings><li>questName-&gt;Excavator Airstrike</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.Letter.label.slateRef": "Guerrilla Attack",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.Letter.text.slateRef": "Wula Empire progressive rebels have sent a guerrilla squad to attack the colony!",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.RandomRaid.customLetterLabel.slateRef": "Wula Empire Progressive Guerrillas",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.RandomRaid.customLetterText.slateRef": "A group of Wula Empire progressive guerrillas is attacking your colony!",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.ResolveQuestDescription.rules.slateRef": "<rulesStrings><li>questDescription-&gt;A group of Wula Empire progressive guerrillas attacked your colony!</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.ResolveQuestName.rules.slateRef": "<rulesStrings><li>questName-&gt;Rebel Guerrillas</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-2.nodes.Letter.label.slateRef": "Assault Fleet Attack",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-2.nodes.Letter.text.slateRef": "The Wula Empire progressive rebels' assault fleet is attacking the colony!",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-2.nodes.ResolveQuestDescription.rules.slateRef": "<rulesStrings><li>questDescription-&gt;The Wula Empire progressive rebels' assault fleet is attacking the colony!</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-2.nodes.ResolveQuestName.rules.slateRef": "<rulesStrings><li>questName-&gt;Assault Fleet Attack</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-3.nodes.ResolveQuestDescription.rules.slateRef": "<rulesStrings><li>questDescription-&gt;Several suspicious black dots have landed near the colony...</li></rulesStrings>",
		"WULA_Progressive_Ship_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-3.nodes.ResolveQuestName.rules.slateRef": "<rulesStrings><li>questName-&gt;Micro Drop Pods</li></rulesStrings>",

		# --- WULA_Hostile_PIA_Attack_Quest ---
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.Letter.label.slateRef": "Fortress Airdrop",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.Letter.text.slateRef": "The Wula Empire Planetary Interdiction Agency has launched a retaliatory strike on the colony. They've airdropped a fully equipped fortress onto your colony!",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.ResolveQuestDescription.rules.slateRef": "<rulesStrings><li>questDescription-&gt;The Wula Empire Planetary Interdiction Agency has launched a retaliatory strike on the colony. They've airdropped a fully equipped fortress onto your colony!</li></rulesStrings>",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-0.nodes.ResolveQuestName.rules.slateRef": "<rulesStrings><li>questName-&gt;Fortress Airdrop</li></rulesStrings>",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.Letter.label.slateRef": "Imperial Troops Assault",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.Letter.text.slateRef": "The Wula Empire Planetary Interdiction Agency has launched a retaliatory strike and sent troops to attack your colony!",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.RandomRaid.customLetterLabel.slateRef": "Imperial Troops Assault",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.RandomRaid.customLetterText.slateRef": "The Wula Empire Planetary Interdiction Agency has launched a retaliatory strike and sent troops to attack your colony!",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.ResolveQuestDescription.rules.slateRef": "<rulesStrings><li>questDescription-&gt;The Wula Empire Planetary Interdiction Agency has launched a retaliatory strike and sent troops to attack your colony!</li></rulesStrings>",
		"WULA_Hostile_PIA_Attack_Quest.root.nodes.RandomNode.nodes.Sequence-1.nodes.ResolveQuestName.rules.slateRef": "<rulesStrings><li>questName-&gt;Imperial Troops Assault</li></rulesStrings>",

		# --- WULA_Intro_NewColony ---
		"WULA_Intro_NewColony.questDescriptionRules.rulesStrings": _li(
			"questDescription->(If you ask how to upgrade Wula tech without reading the prompts, I will kill you.)\n\n"
			"The Wula Empire vanguard has arrived on the surface. The fleet has sent them the first transmission."
		),
		"WULA_Intro_NewColony.questNameRules.rulesStrings": _li("questName->New Colony"),

		# --- WULA_Intro_Spy ---
		"WULA_Intro_Spy.questDescriptionRules.rulesStrings": _li(
			"questDescription->The central control AI of the Wula Empire Planetary Interdiction Agency has sent a request to the colony. A Wula Empire agent has been exposed and is being hunted by other factions. The agent is unarmed and carrying critical information; the colony must shelter them until a Wula Empire shuttle arrives to pick them up. No further details were provided, but she indicated the attacks won't be too severe—the agent has already shaken off most of the pursuers."
		),
		"WULA_Intro_Spy.questNameRules.rulesStrings": _li("questName->Protect the Imperial Agent"),
		"WULA_Intro_Spy.root.nodes.askerArrested.node.nodes.Letter.label.slateRef": "Captured: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerArrested.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has been captured. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.askerDestroyed.node.nodes.Letter.label.slateRef": "Guest Has Died: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerDestroyed.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has died. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.askerLeftBehind.node.nodes.Letter.label.slateRef": "Guest Left Behind: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerLeftBehind.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has been left behind. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.askerLeftMap.node.nodes.Letter.label.slateRef": "Guest Missing: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerLeftMap.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has gone missing in your colony. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.askerRanWild.node.nodes.Letter.label.slateRef": "Guest Missing: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerRanWild.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has returned to the wild in your colony. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.askerSurgeryViolation.node.nodes.Letter.label.slateRef": "Unauthorized Surgery: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerSurgeryViolation.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has undergone an additional unauthorized surgery. Because you violated the agreement first, [asker_pronoun] is leaving. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.askerXenogermAbsorbed.node.nodes.Letter.label.slateRef": "Xenogerm Absorbed: {SUBJECT_definite}",
		"WULA_Intro_Spy.root.nodes.askerXenogermAbsorbed.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting has had a xenogerm absorbed while in your colony. This is a violation; {SUBJECT_pronoun} will leave immediately. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.Delay.node.node.parms.customLetterLabel.value.slateRef": "{BASELABEL} Pursuing [../asker_nameDef]",
		"WULA_Intro_Spy.root.nodes.Delay.node.node.parms.customLetterText.value.slateRef": "{BASETEXT}\n\n[enemyFaction_pawnsPlural] have come here to hunt down [../asker_nameDef].",
		"WULA_Intro_Spy.root.nodes.pickupShipThingDestroyed.node.nodes.Letter.label.slateRef": "Shuttle Destroyed",
		"WULA_Intro_Spy.root.nodes.pickupShipThingDestroyed.node.nodes.Letter.text.slateRef": "The shuttle sent to pick up [asker_nameDef] has been destroyed. [asker_pronoun] will have to walk home. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.pickupShipThingLeftBehind.node.nodes.Letter.label.slateRef": "Shuttle Abandoned",
		"WULA_Intro_Spy.root.nodes.pickupShipThingLeftBehind.node.nodes.Letter.text.slateRef": "The shuttle sent to pick up [asker_nameDef] has been abandoned. [asker_pronoun] will have to walk home.",
		"WULA_Intro_Spy.root.nodes.pickupShipThingSentUnsatisfied.node.nodes.Letter.label.slateRef": "Quest Failed: [resolvedQuestName]",
		"WULA_Intro_Spy.root.nodes.pickupShipThingSentUnsatisfied.node.nodes.Letter.text.slateRef": "The shuttle sent to pick up [asker_nameDef] left behind [asker_objective] for some reason. [asker_pronoun] will have to walk home. [failLetterEndingCommon]",
		"WULA_Intro_Spy.root.nodes.ShuttleDelay.node.nodes.InspectString.inspectString.slateRef": "Should depart by shuttle",
		"WULA_Intro_Spy.root.nodes.ShuttleDelay.node.nodes.Letter.label.slateRef": "Shuttle Arrived",
		"WULA_Intro_Spy.root.nodes.ShuttleDelay.node.nodes.Letter.text.slateRef": "The shuttle sent to pick up [asker_nameDef] has arrived.",

		# --- WULA_Colony_Promotion ---
		"WULA_Colony_Promotion.questDescriptionRules.rulesStrings": _li(
			"questDescription->The colony has accepted an inspection task.\n\n"
			"The central control AI of the Planetary Interdiction Agency has dispatched one of her avatars and an escort squad to your colony. The inspection lasts 12 days. She will evaluate the colony's operations, and you must keep her mood above 25% at all times. Once the inspection is complete, she and the escort will depart by shuttle. If everything goes smoothly, you'll gain a chance to promote your colony and unlock more licensed technologies.\n\n"
			"Be careful: nearby hostile factions already know a VIP has arrived. Raiders will attempt to attack the colony and capture the avatar—you may face heavy assaults!"
		),
		"WULA_Colony_Promotion.questNameRules.rulesStrings": _li("questName->Promotion Task: Colony Inspection"),
		"WULA_Colony_Promotion.root.nodes.askerDestroyed.node.nodes.Letter.label.slateRef": "Guest {SUBJECT_definite} Has Died",
		"WULA_Colony_Promotion.root.nodes.askerDestroyed.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has died. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.askerLeftMap.node.nodes.Letter.label.slateRef": "Guest Missing: {SUBJECT_definite}",
		"WULA_Colony_Promotion.root.nodes.askerLeftMap.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has gone missing in your colony. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.askerRanWild.node.nodes.Letter.label.slateRef": "Guest Missing: {SUBJECT_definite}",
		"WULA_Colony_Promotion.root.nodes.askerRanWild.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has returned to the wild in your colony. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.Delay.node.nodes.Letter.label.slateRef": "Excavator Attack",
		"WULA_Colony_Promotion.root.nodes.Delay.node.nodes.Letter.text.slateRef": "Several fast-moving black dots are streaking toward the colony, trailing thick black smoke... what are those?",
		"WULA_Colony_Promotion.root.nodes.Letter.label.slateRef": "Quest Failed: [resolvedQuestName]",
		"WULA_Colony_Promotion.root.nodes.Letter.text.slateRef": "[faction_name] has become hostile to you.",
		"WULA_Colony_Promotion.root.nodes.lodgersArrested.node.nodes.Letter.label.slateRef": "Guest {SUBJECT_definite} Captured",
		"WULA_Colony_Promotion.root.nodes.lodgersArrested.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has been captured. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.lodgersSurgeryViolation.node.nodes.Letter.label.slateRef": "Unauthorized Surgery: {SUBJECT_definite}",
		"WULA_Colony_Promotion.root.nodes.lodgersSurgeryViolation.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were supposed to protect has undergone an additional unauthorized surgery. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.lodgersXenogermAbsorbed.node.nodes.Letter.label.slateRef": "Xenogerm Absorbed",
		"WULA_Colony_Promotion.root.nodes.lodgersXenogermAbsorbed.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting had a xenogerm absorbed while in your colony. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.MoodBelow.node.nodes.Letter.label.slateRef": "{SUBJECT_bestRoyalTitle} in Low Mood",
		"WULA_Colony_Promotion.root.nodes.MoodBelow.node.nodes.Letter.text.slateRef": "The {SUBJECT_definite} you were charged with protecting and accommodating has had a mood below the minimum threshold [lodgersMoodThreshold] for too long. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.pickupShipThingDestroyed.node.nodes.Letter.label.slateRef": "Shuttle Destroyed",
		"WULA_Colony_Promotion.root.nodes.pickupShipThingDestroyed.node.nodes.Letter.text.slateRef": "The shuttle sent to pick up [asker_faction_leaderTitle] has been destroyed. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.pickupShipThingSentUnsatisfied.node.nodes.Letter.label.slateRef": "Quest Failed: [resolvedQuestName]",
		"WULA_Colony_Promotion.root.nodes.pickupShipThingSentUnsatisfied.node.nodes.Letter.text.slateRef": "The shuttle sent to pick up [asker_faction_leaderTitle] did not receive [asker_objective]. [asker_pronoun] will have to walk home. [failLetterEndingCommon]",
		"WULA_Colony_Promotion.root.nodes.Sequence.nodes.Sequence.nodes.dropoffShipThingDestroyed.node.nodes.Letter.label.slateRef": "Shuttle Destroyed",
		"WULA_Colony_Promotion.root.nodes.Sequence.nodes.Sequence.nodes.dropoffShipThingDestroyed.node.nodes.Letter.text.slateRef": "The shuttle assigned to transport [asker_nameDef] has been destroyed.",
		"WULA_Colony_Promotion.root.nodes.Set-2.value.slateRef": "0.25",
		"WULA_Colony_Promotion.root.nodes.ShuttleDelay.node.nodes.InspectString.inspectString.slateRef": "Should leave by shuttle",
		"WULA_Colony_Promotion.root.nodes.ShuttleDelay.node.nodes.Letter.label.slateRef": "Shuttle Arrived",
		"WULA_Colony_Promotion.root.nodes.ShuttleDelay.node.nodes.Letter.text.slateRef": "A shuttle has arrived to pick up [asker_nameDef].",
	}


def generate(export_xml: Path, out_xml: Path) -> None:
	root = ET.parse(export_xml).getroot()
	translations = build_translations()

	out_root = ET.Element("LanguageData")
	for el in list(root):
		key = el.tag
		if key not in translations:
			raise KeyError(f"Missing translation for export key: {key}")

		value = translations[key]
		out_el = ET.SubElement(out_root, key)
		if isinstance(value, list):
			for item in value:
				_assert_no_han(item, key)
				li = ET.SubElement(out_el, "li")
				li.text = item
		else:
			_assert_no_han(value, key)
			out_el.text = value

	_indent(out_root)
	tree = ET.ElementTree(out_root)
	out_xml.parent.mkdir(parents=True, exist_ok=True)
	tree.write(out_xml, encoding="utf-8", xml_declaration=True)


def main(argv: list[str]) -> int:
	parser = argparse.ArgumentParser()
	parser.add_argument("--export", type=Path, required=True)
	parser.add_argument("--out", type=Path, required=True)
	args = parser.parse_args(argv)

	generate(args.export, args.out)
	return 0


if __name__ == "__main__":
	raise SystemExit(main(sys.argv[1:]))
