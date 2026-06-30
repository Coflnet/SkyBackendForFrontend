using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;
using AwesomeAssertions;
using MessagePack;
using Microsoft.AspNetCore.JsonPatch.Internal;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class InventoryParserTests
{
    string jsonSample = """
        {
    "_events": {},
    "_eventsCount": 0,
    "id": 0,
    "type": "minecraft:inventory",
    "title": "Inventory",
    "slots": [
        null,
        null,
        null,
        null,
        {
            "type": 160,
            "count": 1,
            "metadata": 7,
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "display": {
                        "type": "compound",
                        "value": {
                            "Lore": {
                                "type": "list",
                                "value": {
                                    "type": "string",
                                    "value": [
                                        "§7This slot may be changed to a",
                                        "§7shortcut for your favorite game",
                                        "§7or mode!",
                                        "",
                                        "§7You may change this slot at any",
                                        "§7time using Right Click."
                                    ]
                                }
                            },
                            "Name": {
                                "type": "string",
                                "value": "§eCustom Slot"
                            }
                        }
                    }
                }
            },
            "name": "stained_glass_pane",
            "displayName": "Stained Glass Pane",
            "stackSize": 64,
            "slot": 16
        },
        {"type":315,"count":1,"metadata":0,"nbt":{"type":"compound","name":"","value":{"ench":{"type":"list","value":{"type":"end","value":[]}},"Unbreakable":{"type":"byte","value":1},"HideFlags":{"type":"int","value":254},
            "display":{"type":"compound","value":{"Lore":{"type":"list","value":{"type":"string","value":["§7Health: §a+200","§7Defense: §a+150","§7Mining Speed: §a+230 §9(+60) §d(+90)"]}},"Name":{"type":"string","value":"§f§f§dJaded Chestplate of Divan"}}},
            "ExtraAttributes":{"type":"compound","value":{"rarity_upgrades":{"type":"int","value":1},"gems":{"type":"compound","value":{"JADE_1":{"type":"string","value":"FINE"},"JADE_0":{"type":"string","value":"FINE"},
                "unlocked_slots":{"type":"list","value":{"type":"string","value":["JADE_0","JADE_1","TOPAZ_0","AMBER_0","AMBER_1"]}},"AMBER_0":{"type":"string","value":"FINE"},"AMBER_1":{"type":"string","value":"FINE"},"TOPAZ_0":{"type":"string","value":"FINE"}}},"modifier":{"type":"string","value":"jaded"},"id":{"type":"string","value":"DIVAN_CHESTPLATE"},
                "enchantments":{"type":"compound","value":{"ultimate_wisdom":{"type":"int","value":1},"growth":{"type":"int","value":5},"protection":{"type":"int","value":5}}},"uuid":{"type":"string","value":"ea533251-6328-4a3c-8477-649cfb93ff45"},"timestamp":{"type":"string","value":"7/24/23 2:42 AM"}}}}},
                "stackId":null,"name":"golden_chestplate","displayName":"Golden Chestplate","stackSize":1,"maxDurability":112,"slot":32},
        {
            "type": 306,
            "count": 1,
            "metadata": 0,
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "ench": {
                        "type": "list",
                        "value": {
                            "type": "end",
                            "value": []
                        }
                    },
                    "Unbreakable": {
                        "type": "byte",
                        "value": 1
                    },
                    "HideFlags": {
                        "type": "int",
                        "value": 254
                    },
                    "display": {
                        "type": "compound",
                        "value": {
                            "Lore": {
                                "type": "list",
                                "value": {
                                    "type": "string",
                                    "value": [
                                        "┬º7Defense: ┬ºa+10",
                                        "",
                                        "┬º7Growth I",
                                        "┬º7Grants ┬ºa+15 ┬ºcÔØñ Health┬º7.",
                                        "",
                                        "┬º7┬ºcYou do not have a high enough",
                                        "┬ºcEnchanting level to use some of",
                                        "┬ºcthe enchantments on this item!",
                                        "",
                                        "┬º7┬º8This item can be reforged!",
                                        "┬ºf┬ºlCOMMON HELMET"
                                    ]
                                }
                            },
                            "Name": {
                                "type": "string",
                                "value": "┬ºfIron Helmet"
                            }
                        }
                    },
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "id": {
                                "type": "string",
                                "value": "PET"
                            },
                            "gems": {
                                "type": "compound",
                                "value": {
                                    "JADE_0": {
                                        "type": "string",
                                        "value": "FINE"
                                    },
                                    "COMBAT_0": {
                                        "type": "compound",
                                        "value": {
                                            "uuid": {
                                                "type": "string",
                                                "value": "a5c233ba-9554-4c80-a697-1a78c66c045d"
                                            },
                                            "quality": {
                                                "type": "string",
                                                "value": "PERFECT"
                                            }
                                        }
                                    }
                                }
                            },
                            "ability_scroll": {
                                "type": "list",
                                "value": {
                                    "type": "string",
                                    "value": [
                                        "WITHER_SHIELD_SCROLL",
                                        "SHADOW_WARP_SCROLL"
                                    ]
                                }
                            },
                            "enchantments": {
                                "type": "compound",
                                "value": {
                                    "growth": {
                                        "type": "int",
                                        "value": 1
                                    }
                                }
                            },
                            "uuid": {
                                "type": "string",
                                "value": "0cf52647-c130-43ec-9c46-e2dc162d4894"
                            },
                            "modifier": {
                                "type": "string",
                                "value": "heavy"
                            },
                            "mined_crops": {
                                "type": "long",
                                "value": [
                                    1,
                                    8314091
                                ]
                            },
                            "sinker": {
                                "type": "compound",
                                "value": {
                                    "part": {
                                        "type": "string",
                                        "value": "stingy_sinker"
                                    },
                                    "uuid": {
                                        "type": "string",
                                        "value": "7e329f9e-8aaa-4a8c-90ee-f6bbb4b8d753"
                                    }
                                }
                            },
                            "necromancer_souls": {
                                "type": "list",
                                "value": {
                                    "type": "compound",
                                    "value": [
                                        {
                                            "mob_id": {
                                                "type": "string",
                                                "value": "MASTER_CRYPT_TANK_ZOMBIE_70"
                                            }
                                        },
                                        {
                                            "mob_id": {
                                                "type": "string",
                                                "value": "MASTER_CRYPT_TANK_ZOMBIE_70"
                                            }
                                        },
                                        {
                                            "mob_id": {
                                                "type": "string",
                                                "value": "MASTER_CRYPT_TANK_ZOMBIE_70"
                                            }
                                        },
                                        {
                                            "mob_id": {
                                                "type": "string",
                                                "value": "MASTER_CRYPT_TANK_ZOMBIE_70"
                                            }
                                        },
                                        {
                                            "mob_id": {
                                                "type": "string",
                                                "value": "MASTER_CRYPT_TANK_ZOMBIE_70"
                                            }
                                        },
                                        {
                                            "mob_id": {
                                                "type": "string",
                                                "value": "MASTER_CRYPT_TANK_ZOMBIE_70"
                                            }
                                        }
                                    ]
                                }
                            },
                            "petInfo": {
                                "type": "string",
                                "value": "{\"type\":\"ELEPHANT\",\"active\":false,\"exp\":3.397827122665796E7,\"tier\":\"LEGENDARY\",\"hideInfo\":false,\"heldItem\":\"PET_ITEM_FARMING_SKILL_BOOST_EPIC\",\"candyUsed\":10,\"uuid\":\"8760755f-f72b-4624-8cf2-c51b21e35acc\",\"hideRightClick\":false}"
                            },
                            "timestamp": {
                                "type": "string",
                                "value": "2/18/23 4:27 AM"
                            }
                        }
                    }
                }
            },
            "name": "iron_helmet",
            "displayName": "Iron Helmet",
            "stackSize": 1,
            "slot": 5
        }
	  ]
}
""";
    [Test]
    public void Parse()
    {
        var parser = new InventoryParser();
        var serialized = MessagePackSerializer.Serialize(parser.Parse(jsonSample));
        var deserialized = MessagePackSerializer.Deserialize<List<SaveAuction>>(serialized);
        var item = deserialized
                        .Where(i => i != null).Last();
        Console.WriteLine(JsonConvert.SerializeObject(item, Formatting.Indented));
        Assert.That("PET_ELEPHANT", Is.EqualTo(item.Tag));
        Assert.That("┬ºfIron Helmet", Is.EqualTo(item.ItemName));
        Assert.That(1, Is.EqualTo(item.Enchantments.Count));
        Assert.That(1, Is.EqualTo(item.Enchantments.Where(e => e.Type == Core.Enchantment.EnchantmentType.growth).First().Level));
        Assert.That("0cf52647-c130-43ec-9c46-e2dc162d4894", Is.EqualTo(item.FlatenedNBT["uuid"]));
        Assert.That("PET_ITEM_FARMING_SKILL_BOOST_EPIC", Is.EqualTo(item.FlatenedNBT["heldItem"]));
        Assert.That("33978271,22665796", Is.EqualTo(item.FlatenedNBT["exp"].Replace('.', ',')));
        Assert.That("FINE", Is.EqualTo(item.FlatenedNBT["JADE_0"]));
        Assert.That("PERFECT", Is.EqualTo(item.FlatenedNBT["COMBAT_0"]));
        Assert.That("4303281387", Is.EqualTo(item.FlatenedNBT["mined_crops"]));
        Assert.That("SHADOW_WARP_SCROLL WITHER_SHIELD_SCROLL", Is.EqualTo(item.FlatenedNBT["ability_scroll"]));
        item.FlatenedNBT["COMBAT_0.uuid"].Should().Match("a5c233ba-9554-4c80-a697-1a78c66c045d");
        Assert.That("6", Is.EqualTo(item.FlatenedNBT["MASTER_CRYPT_TANK_ZOMBIE_70"]));
        Assert.That(ItemReferences.Reforge.Heavy, Is.EqualTo(item.Reforge));
        Assert.That(Tier.COMMON, Is.EqualTo(item.Tier));
        item.FlatenedNBT["sinker.uuid"].Should().Be("7e329f9e-8aaa-4a8c-90ee-f6bbb4b8d753");
        item.FlatenedNBT["sinker.part"].Should().Be("stingy_sinker");

        var divan = deserialized.Where(i => i != null).Skip(1).First();
        Assert.That(new DateTime(2023, 7, 24), Is.EqualTo(divan.ItemCreatedAt.Date));
        Assert.That("AMBER_0,AMBER_1,JADE_0,JADE_1,TOPAZ_0", Is.EqualTo(divan.FlatenedNBT["unlocked_slots"]));
    }

    string petSample = """
        {
    "_events": {},
    "_eventsCount": 0,
    "id": 0,
    "type": "minecraft:inventory",
    "title": "Inventory",
    "slots": [
{
  "type": 397,
  "count": 1,
  "metadata": 3,
  "nbt": {
    "type": "compound",
    "name": "",
    "value": {
      "HideFlags": {
        "type": "int",
        "value": 254
      },
      "SkullOwner": {
        "type": "compound",
        "value": {
          "Id": {
            "type": "string",
            "value": "3a80504c-0993-3be5-92cd-2200f94a72b6"
          },
          "Properties": {
            "type": "compound",
            "value": {
              "textures": {
                "type": "list",
                "value": {
                  "type": "compound",
                  "value": [
                    {
                      "Value": {
                        "type": "string",
                        "value": "ewogICJ0aW1lc3RhbXAiIDogMTYwNjQyODA5NTg3NywKICAicHJvZmlsZUlkIiA6ICIzZmM3ZmRmOTM5NjM0YzQxOTExOTliYTNmN2NjM2ZlZCIsCiAgInByb2ZpbGVOYW1lIiA6ICJZZWxlaGEiLAogICJzaWduYXR1cmVSZXF1aXJlZCIgOiB0cnVlLAogICJ0ZXh0dXJlcyIgOiB7CiAgICAiU0tJTiIgOiB7CiAgICAgICJ1cmwiIDogImh0dHA6Ly90ZXh0dXJlcy5taW5lY3JhZnQubmV0L3RleHR1cmUvZjVmMjlhOTc1NTI5Mjc2ZDkxNmZjNjc5OTg4MzNjMTFlZTE3OGZmMjFlNTk0MWFmZGZiMGZhNzAxMGY4Mzc0ZSIKICAgIH0KICB9Cn0"
                      }
                    }
                  ]
                }
              }
            }
          }
        }
      },
      "display": {
        "type": "compound",
        "value": {
          "Lore": {
            "type": "list",
            "value": {
              "type": "string",
              "value": [
                "§8Fishing Pet, Grown-up Skin",
                "",
                "§b§lMAX LEVEL",
                "",
                "§7§eRight-click to add this pet to",
                "§eyour pet menu!",
                "",
                "§6§lLEGENDARY",
                "§8§m-----------------",
                "§7Seller: §b[MVP§e+§b] Parakkz",
                "§7Buy it now: §6190,000,000 coins",
                "",
                "§7Ends in: §e23h",
                "",
                "§eClick to inspect!"
              ]
            }
          },
          "Name": {
            "type": "string",
            "value": "§f§f§7[Lvl 100] §6Baby Yeti ✦"
          }
        }
      },
      "ExtraAttributes": {
        "type": "compound",
        "value": {
          "petInfo": {
            "type": "string",
            "value": "{\"type\":\"BABY_YETI\",\"active\":false,\"exp\":3.892246997045735E7,\"tier\":\"LEGENDARY\",\"hideInfo\":false,\"heldItem\":\"DWARF_TURTLE_SHELMET\",\"candyUsed\":10,\"skin\":\"YETI_GROWN_UP\",\"uuid\":\"28fdb3cb-1029-48cf-9591-3e7d8d67ba9a\",\"hideRightClick\":false}"
          },
          "id": {
            "type": "string",
            "value": "PET"
          },
          "uuid": {
            "type": "string",
            "value": "28fdb3cb-1029-48cf-9591-3e7d8d67ba9a"
          },
          "timestamp": {
            "type": "long",
            "value": [
                391,
                733787264
            ]
          }
        }
      }
    }
  },
  "stackId": null,
  "name": "skull",
  "displayName": "Head",
  "stackSize": 64,
  "slot": 11
},
{
    "type": 397,
    "count": 1,
    "metadata": 3,
    "nbt": {
        "type": "compound",
        "name": "",
        "value": {
            "HideFlags": {
                "type": "int",
                "value": 254
            },
            "SkullOwner": {
                "type": "compound",
                "value": {
                    "Id": {
                        "type": "string",
                        "value": "949f3bb5-f8a7-301e-91f8-a19bc35d59f4"
                    },
                    "hypixelPopulated": {
                        "type": "byte",
                        "value": 1
                    },
                    "Properties": {
                        "type": "compound",
                        "value": {
                            "textures": {
                                "type": "list",
                                "value": {
                                    "type": "compound",
                                    "value": [
                                        {
                                            "Signature": {
                                                "type": "string",
                                                "value": "IZzSdvJDh05oZbk1zJ/+k9mCt/PCYbonXoo6uCJyaL+Ioepj1V/Ioah15C9qkagnftxSMaXZ+l1MBXkAeP6RFFZafQf4DEH2pQb2Ftg2ivDxom0jBiheS7PPPNRfv7LTPxaqyttL8CZqHi8C05HNrrmgL37x/hnBblcMY4iPaiFliZyxQnSc/7y7vijrk6oOREAHv+v6uIY270MxeJUH7puHv+K30baJZiXkm0kKVpTRYxxcEKznS+7dCG4lJpDlB8MtBMl1cMWjrmf+MWRUsvVBnjHdjbVSDKLa9w7ZQ4DfgrDWkB3jOIqS4DZzzz60KWFyQfy/RKrxwYs8vEndWhGURbKe1F446Qy35ZbLfZvKlx/Xe3Mgo7ws8IpTksUMMgnXwyZdqh05u3yLr/INzq985uURtvl0ASGQyYW0Wla2ipBwewCkcBPxSjcQvFXJTULO/a1XAM/iiSyUhpwGOrIxWY9enliKqps6isjIP6QDCzOTTZPi7XZNzal3CzPSIWtuMuxrQY1F9EKq2oyyisYPzZkD0HP8lWAigHKX3ssxA9ch61RrnKoWqytH/CaB65V9hjo+6J+CUpjBYSPLSJYuH76e5n8mLNQzjOTjPxegsKB+C1oqh6k2XE2Sz9lddS9cjKgBdXqlsAqSIly6ChvYUZcizoeMMbqqW4XxfwQ="
                                            },
                                            "Value": {
                                                "type": "string",
                                                "value": "ewogICJ0aW1lc3RhbXAiIDogMTYxMTIyNjQyNTg4MCwKICAicHJvZmlsZUlkIiA6ICIzYTNmNzhkZmExZjQ0OTllYjE5NjlmYzlkOTEwZGYwYyIsCiAgInByb2ZpbGVOYW1lIiA6ICJUaGVyb2Ryb2dvIiwKICAic2lnbmF0dXJlUmVxdWlyZWQiIDogdHJ1ZSwKICAidGV4dHVyZXMiIDogewogICAgIlNLSU4iIDogewogICAgICAidXJsIiA6ICJodHRwOi8vdGV4dHVyZXMubWluZWNyYWZ0Lm5ldC90ZXh0dXJlL2QxNWFmYWQ1M2QxNzI2ZmFlODZiM2ZiMTFiYTAxZTUxMTEyYjEwNTVlOGU1YWY4YjdkZjg2ZWY5NTZmMWQ0YTEiLAogICAgICAibWV0YWRhdGEiIDogewogICAgICAgICJtb2RlbCIgOiAic2xpbSIKICAgICAgfQogICAgfQogIH0KfQ=="
                                            }
                                        }
                                    ]
                                }
                            }
                        }
                    }
                }
            },
            "display": {
                "type": "compound",
                "value": {
                    "Lore": {
                        "type": "list",
                        "value": {
                            "type": "string",
                            "value": [
                                "8Combat Pet",
                                "",
                                "7Defense: a+0.5",
                                "7Strength: c+0.5",
                                "7Ferocity: a+0.05",
                                "",
                                "6Bacon Farmer",
                                "77Pig minions work a0.3% 7faster while on",
                                "7your island",
                                "",
                                "6Pork Master",
                                "77Buffs the 6Pigman Sword 7by a0.4 c",
                                "cDamage 7and a0.2 c Strength7.",
                                "",
                                "6Giant Slayer",
                                "77Deal c+50% 7damage to monsters Level",
                                "7a50+7 and c+75% 7damage to monsters",
                                "7Level a100+7.",
                                "",
                                "7Progress to Level 2: e0%",
                                "flm                         r e06/e660",
                                "",
                                "7eRight-click to add this pet to your",
                                "epet menu!",
                                "",
                                "6lLEGENDARY"
                            ]
                        }
                    },
                    "Name": {
                        "type": "string",
                        "value": "7[Lvl 1] 6Pigman"
                    }
                }
            },
            "ExtraAttributes": {
                "type": "compound",
                "value": {
                    "petInfo": {
                        "type": "string",
                        "value": "{\"type\":\"PIGMAN\",\"active\":false,\"exp\":0.0,\"tier\":\"LEGENDARY\",\"hideInfo\":false,\"candyUsed\":0,\"hideRightClick\":false,\"noMove\":false}"
                    },
                    "id": {
                        "type": "string",
                        "value": "PET"
                    },
                    "uuid": {
                        "type": "string",
                        "value": "9a8e20bd-12dd-46f1-8e4c-061b187bcba4"
                    },
                    "timestamp": {
                        "type": "long",
                        "value": [
                            404,
                            741927295
                        ]
                    }
                }
            }
        }
    }
}
]}
""";

    [Test]
    public void ParsePetRarity()
    {
        var parser = new InventoryParser();
        var serialized = MessagePackSerializer.Serialize(parser.Parse(petSample));
        var parsed = MessagePackSerializer.Deserialize<List<SaveAuction>>(serialized);
        var item = parsed.First();
        Assert.That(Tier.LEGENDARY, Is.EqualTo(item.Tier));
        Assert.That(new DateTime(2023, 3, 29), Is.EqualTo(item.ItemCreatedAt.Date));
        parsed.Last().Tier.Should().Be(Tier.LEGENDARY);
    }



    private string jsonSampleCT = """
[{"id":"minecraft:tnt","Count":2,"tag":{"ench":[],"HideFlags":254,"display":{"Lore":["§7Breaks weak walls. Can be used","§7to blow up Crypts in §cThe","§cCatacombs §7and §3Crystal","§3Hollows§7.","","§9§lRARE"],
"Name":"§9Superboom TNT"},"ExtraAttributes":{"id":"SUPERBOOM_TNT"}},"Damage":0},
{"id":"minecraft:stained_glass","Count":1,"tag":{"HideFlags":254,"display":{"Lore":["§7§oA rare space helmet forged","§7§ofrom shards of moon glass.","","§7§8This item can be reforged!","§c§lSPECIAL HELMET","pricePaid xyy"],
"Name":"§cSpace Helmet"},"ExtraAttributes":{"id":"DCTR_SPACE_HELM","uuid":"b14aefbd-cbf8-4ca1-aa2e-5c0422807c60","timestamp":"4/8/23 10:01 AM", enchantments:{impaling:3,chance:4,piercing:1,infinite_quiver:10,ultimate_soul_eater:5,snipe:3,telekinesis:1,power:7},
"gems": {
                "JASPER_0": {
                    "uuid": "2441955e-8b8b-4373-914f-985077e6f530",
                    "quality": "FLAWLESS"
                },
                "COMBAT_0": {
                    "uuid": "f006fc85-4a65-4ecb-980f-0a8dcdf61713",
                    "quality": "FLAWLESS"
                },
                "unlocked_slots": [
                    "JASPER_0",
                    "COMBAT_0"
                ],
                "COMBAT_0_gem": "JASPER"
            }}},"Damage":14}]
""";
    [Test]
    public void ParseCT()
    {
        var parser = new InventoryParser();
        var serialized = MessagePackSerializer.Serialize(parser.Parse(jsonSampleCT));
        var item = MessagePackSerializer.Deserialize<List<SaveAuction>>(serialized)
                        .Where(i => i != null).Last();
        Assert.That("DCTR_SPACE_HELM", Is.EqualTo(item.Tag));
        Assert.That("§cSpace Helmet", Is.EqualTo(item.ItemName));
        Assert.That(ItemReferences.Reforge.None, Is.EqualTo(item.Reforge));
        Assert.That(8, Is.EqualTo(item.Enchantments.Count));
        Assert.That(3, Is.EqualTo(item.Enchantments.Where(e => e.Type == Core.Enchantment.EnchantmentType.impaling).First().Level));
        Assert.That(4, Is.EqualTo(item.Enchantments.Where(e => e.Type == Core.Enchantment.EnchantmentType.chance).First().Level));
        Assert.That(1, Is.EqualTo(item.Count));
        Assert.That("b14aefbd-cbf8-4ca1-aa2e-5c0422807c60", Is.EqualTo(item.FlatenedNBT["uuid"]));
        item.ItemCreatedAt.Should().Be(new DateTime(2023, 4, 8, 10, 1, 0));
        Assert.That(Tier.SPECIAL, Is.EqualTo(item.Tier));
    }

    private string ctTimestamp = """
    [{"id":"minecraft:iron_sword","Count":1,"tag":{"Unbreakable":1,"HideFlags":254,"display":{"Lore":["§6§lLEGENDARY DUNGEON SWORD"],"Name":"§f§f§6Livid Dagger"},
        "ExtraAttributes":{"id":"LIVID_DAGGER","uuid":"fd0eb7f0-67e1-4747-8210-2f2d4574a1a0","timestamp":1734209309113}},"Damage":0}]
    """;

    [Test]
    public void ParseCTTime()
    {
        var parser = new InventoryParser();
        var serialized = MessagePackSerializer.Serialize(parser.Parse(ctTimestamp));
        var item = MessagePackSerializer.Deserialize<List<SaveAuction>>(serialized)
                        .Where(i => i != null).Last();
        Assert.That(item.ItemCreatedAt, Is.EqualTo(new DateTime(2024, 12, 14, 20, 48, 29, 113, DateTimeKind.Utc)));
    }

    /// <summary>
    /// Some hypixel item tags are split up in multiple virtual items
    /// The parsing needs to reflect that
    /// </summary>
    [Test]
    public void SpecialItemIdParsing()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
        {
        "title": "Inventory",
        "slots": [
            {
            "count": 1,
            "metadata": 0,
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "potion_level": {
                                "type": "int",
                                "value": 5
                            },
                            "potion": {
                                "type": "string",
                                "value": "harvest_harbinger"
                            },
                            "potion_type": {
                                "type": "string",
                                "value": "POTION"
                            },
                            "id": {
                                "type": "string",
                                "value": "POTION"
                            }
                        }
                    }
                }
            }},
            {
            "count": 1,
            "metadata": 0,
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "runes": {
                                "type": "compound",
                                "value": {
                                    "ICE_SKATES": {
                                        "type": "int",
                                        "value": 3
                                    }
                                }
                            },
                            "id": {
                                "type": "string",
                                "value": "UNIQUE_RUNE"
                            }
                        }
                    }
                }
            }}
        ]}
        """);
        Assert.That(data.First().Tag, Is.EqualTo("POTION_harvest_harbinger"));
        Assert.That(data.Last().Tag, Is.EqualTo("UNIQUE_RUNE_ICE_SKATES"));
        Console.WriteLine(JsonConvert.SerializeObject(data.Last().FlatenedNBT, Formatting.Indented));
        Assert.That(data.Last().FlatenedNBT["RUNE_ICE_SKATES"], Is.EqualTo("3"));
    }

    [Test]
    public void Parse117Strings()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
        {
        "_events": {},
        "_eventsCount": 0,
        "id": 0,
        "type": "minecraft:inventory",
        "title": "Inventory",
        "slots": [
            {
            "type": 746,
            "count": 1,
            "metadata": 0,
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "Unbreakable": {
                        "type": "byte",
                        "value": 1
                    },
                    "HideFlags": {
                        "type": "int",
                        "value": 255
                    },
                    "display": {
                        "type": "compound",
                        "value": {
                            "Lore": {
                                "type": "list",
                                "value": {
                                    "type": "string",
                                    "value": [
                                        "{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"Defense: \"},{\"text\":\" \"},{\"color\":\"green\",\"text\":\"+10\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"Growth I\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"Grants \"},{\"color\":\"green\",\"text\":\"+15 \"},{\"color\":\"red\",\"text\":\"❤ Health\"},{\"color\":\"gray\",\"text\":\".\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"\"},{\"color\":\"red\",\"text\":\"You do not have a high enough\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"color\":\"red\",\"text\":\"Enchanting level to use some of\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"color\":\"red\",\"text\":\"the enchantments on this item!\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"\"},{\"color\":\"dark_gray\",\"text\":\"This item can be reforged!\"}],\"text\":\"\"}",
                                        "{\"italic\":false,\"extra\":[{\"bold\":true,\"color\":\"white\",\"text\":\"COMMON HELMET\"}],\"text\":\"\"}"
                                    ]
                                }
                            },
                            "Name": {
                                "type": "string",
                                "value": "{\"italic\":false,\"extra\":[{\"color\":\"white\",\"text\":\"Iron Helmet\"}],\"text\":\"\"}"
                            }
                        }
                    },
                    "Enchantments": {
                        "type": "list",
                        "value": {
                            "type": "compound",
                            "value": [
                                {
                                    "lvl": {
                                        "type": "short",
                                        "value": 0
                                    },
                                    "id": {
                                        "type": "string",
                                        "value": "minecraft:protection"
                                    }
                                }
                            ]
                        }
                    },
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "id": {
                                "type": "string",
                                "value": "IRON_HELMET"
                            },
                            "enchantments": {
                                "type": "compound",
                                "value": {
                                    "growth": {
                                        "type": "int",
                                        "value": 1
                                    }
                                }
                            },
                            "uuid": {
                                "type": "string",
                                "value": "0cf52647-c130-43ec-9c46-e2dc162d4894"
                            },
                            "timestamp": {
                                "type": "string",
                                "value": "2/18/23 4:27 AM"
                            }
                        }
                    }
                }
            },
            "name": "iron_helmet",
            "displayName": "Iron Helmet",
            "stackSize": 1,
            "slot": 5
            },
        ]}
        """);

        Assert.That("§fIron Helmet", Is.EqualTo(data.First().ItemName));
        Assert.That(data.First().Context["lore"].StartsWith("§7Defense:  §a+10\n\n§7Growth I\n§7Grants §a+15 §c❤ Health"));
        Assert.That(new DateTime(2023, 2, 18), Is.EqualTo(data.First().ItemCreatedAt.Date));
    }

    [Test]
    public void DoNotCrashForVanilla()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
        {
        "_events": {},
        "_eventsCount": 0,
        "id": 0,
        "type": "minecraft:inventory",
        "slots":[
            {"type":844,"count":1,"metadata":0,"nbt":{"type":"compound","name":"","value":{"HideFlags":{"type":"int","value":254},
            "display":{"type":"compound","value":{"Lore":{"type":"list","value":{"type":"string","value":["{\"italic\":false,\"extra\":[{\"color\":\"dark_gray\",\"text\":\"Bed Wars\"}],\"text\":\"\"}","{\"italic\":false,\"text\":\"\"}","{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"Join a game of 4v4v4v4.\"}],\"text\":\"\"}","{\"italic\":false,\"text\":\"\"}","{\"italic\":false,\"extra\":[{\"color\":\"green\",\"text\":\"► Click to Play\"}],\"text\":\"\"}","{\"italic\":false,\"extra\":[{\"color\":\"gray\",\"text\":\"1,171 currently playing!\"}],\"text\":\"\"}","{\"italic\":false,\"text\":\"\"}","{\"italic\":false,\"extra\":[{\"color\":\"dark_gray\",\"text\":\"Right Click to change this slot!\"}],\"text\":\"\"}"]}},
            "Name":{"type":"string","value":"{\"italic\":false,\"extra\":[{\"color\":\"green\",\"text\":\"4v4v4v4 \"}],\"text\":\"\"}"}}},"ExtraAttributes":{"type":"compound","value":{"animatedItemIdentifier":{"type":"string","value":"c7925960-db90-404a-aaf1-546f776bcc24"}}},"Damage":{"type":"int","value":14}}},"stackId":null,"name":"red_bed","displayName":"Red Bed","stackSize":1,"slot":14}
        ]}
        """);
        Assert.That(data.First()?.ItemName, Is.Null);
    }

    [Test]
    public void ParseAttributeShard()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
        {
        "title": "Inventory",
        "slots": [
            {
            "count": 64,
            "metadata": 0,
            "displayName": "Glacite Walker Shard",
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "id": {
                                "type": "string",
                                "value": "ATTRIBUTE_SHARD"
                            },
                            "ice_essence": {
                                "type": "int",
                                "value": 1
                            },
                            "uuid": {
                                "type": "string",
                                "value": "1952871256"
                            }
                        }
                    }
                }
            }},
            null,
            {
            "count": 4,
            "metadata": 0,
            "displayName": "Piranha Shard",
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "id": {
                                "type": "string",
                                "value": "ATTRIBUTE_SHARD"
                            },
                            "moonglade_serendipity": {
                                "type": "int",
                                "value": 1
                            },
                            "uuid": {
                                "type": "string",
                                "value": "1564512704"
                            }
                        }
                    }
                }
            }}
        ]}
        """);
        var first = data.First();
        Assert.That(first.Tag, Is.EqualTo("SHARD_GLACITE_WALKER"));
        Assert.That(first.Count, Is.EqualTo(64));
        Assert.That(first.FlatenedNBT["ice_essence"], Is.EqualTo("1"));

        var second = data.Skip(2).First();
        Assert.That(second.Tag, Is.EqualTo("SHARD_PIRANHA"));
        Assert.That(second.Count, Is.EqualTo(4));
        Assert.That(second.FlatenedNBT["moonglade_serendipity"], Is.EqualTo("1"));
    }

    [Test]
    public void TryGetShardTagFromNameTest()
    {
        Assert.That(InventoryParser.TryGetShardTagFromName("§9Glacite Walker Shard", out var tag), Is.True);
        Assert.That(tag, Is.EqualTo("SHARD_GLACITE_WALKER"));

        Assert.That(InventoryParser.TryGetShardTagFromName("§9Piranha Shard", out var tag2), Is.True);
        Assert.That(tag2, Is.EqualTo("SHARD_PIRANHA"));

        Assert.That(InventoryParser.TryGetShardTagFromName("§fCoralot Shard", out var tag3), Is.True);
        Assert.That(tag3, Is.EqualTo("SHARD_CORALOT"));
    }

    [Test]
    public void ParseAzaleaFormat()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
                {
                    "slots": [
                        null,
                        {
                            "count": 2,
                            "name": "minecraft:player_head",
                            "slot": 36,
                            "nbt": {
                                "minecraft:custom_data": {
                                    "id": "IRON_HELMET",
                                    "uuid": "azalea-item-uuid",
                                    "attributes": {
                                        "mending": 1
                                    }
                                },
                                "minecraft:custom_name": {
                                    "extra": [
                                        {
                                            "color": "gold",
                                            "text": "Kraken Shard"
                                        }
                                    ],
                                    "italic": false,
                                    "text": ""
                                },
                                "minecraft:lore": [
                                    {
                                        "extra": [
                                            {
                                                "bold": true,
                                                "color": "gold",
                                                "text": "LEGENDARY"
                                            }
                                        ],
                                        "italic": false,
                                        "text": ""
                                    }
                                ]
                            }
                        }
                    ]
                }
                """).ToList();

        data.Should().HaveCount(2);
        data[0].Should().BeNull();
        var item = data[1];
        item.Should().NotBeNull();
        item.Tag.Should().Be("IRON_HELMET");
        item.Count.Should().Be(2);
        item.ItemName.Should().Be("§6Kraken Shard");
        item.Uuid.Should().Be("azalea-item-uuid");
        item.FlatenedNBT.Values.Should().Contain("1");
        item.Tier.Should().Be(Tier.LEGENDARY);
    }

    [Test]
    public void ParseAzaleaFormatWithStringInExtra()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
                {
                    "slots": [
                        {
                            "count": 1,
                            "metadata": 0,
                            "name": "minecraft:diamond_shovel",
                            "nbt": {
                                "minecraft:custom_data": {
                                    "enchantments": {
                                        "ultimate_wise": 5
                                    },
                                    "ethermerge": 1,
                                    "id": "ASPECT_OF_THE_VOID",
                                    "timestamp": 1769397255146,
                                    "uuid": "f5daa699-54d3-411b-b40a-f7e2f9f67aca"
                                },
                                "minecraft:custom_name": {
                                    "extra": [
                                        {
                                            "color": "dark_purple",
                                            "text": "Aspect of the Void"
                                        }
                                    ],
                                    "italic": false,
                                    "text": ""
                                },
                                "minecraft:enchantment_glint_override": true,
                                "minecraft:lore": [
                                    {
                                        "extra": [
                                            " ",
                                            {
                                                "color": "dark_gray",
                                                "text": "["
                                            },
                                            {
                                                "color": "gray",
                                                "text": "✎"
                                            },
                                            {
                                                "color": "dark_gray",
                                                "text": "]"
                                            }
                                        ],
                                        "italic": false,
                                        "text": ""
                                    },
                                    {
                                        "extra": [
                                            {
                                                "bold": true,
                                                "color": "dark_purple",
                                                "text": "EPIC SWORD"
                                            }
                                        ],
                                        "italic": false,
                                        "text": ""
                                    }
                                ],
                                "minecraft:tooltip_display": {
                                    "hidden_components": [
                                        "minecraft:jukebox_playable"
                                    ]
                                },
                                "minecraft:unbreakable": null
                            },
                            "slot": 41,
                            "type": 937
                        }
                    ]
                }
                """).ToList();

        data.Should().HaveCount(1);
        var item = data[0];
        item.Should().NotBeNull();
        item.Tag.Should().Be("ASPECT_OF_THE_VOID");
        item.ItemName.Should().Be("§5Aspect of the Void");
        item.Tier.Should().Be(Tier.EPIC);
        item.Uuid.Should().Be("f5daa699-54d3-411b-b40a-f7e2f9f67aca");
        item.FlatenedNBT["uuid"].Should().Be("f5daa699-54d3-411b-b40a-f7e2f9f67aca");
    }

    [Test]
    public void ParseAzaleaPetTier()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
                {
                    "slots": [
                        {
                            "count": 1,
                            "displayName": "[Lvl 94] Enderman",
                            "name": "minecraft:player_head",
                            "slot": 10,
                            "tag": "PET_ENDERMAN",
                            "type": 1235,
                            "nbt": {
                                "minecraft:custom_data": {
                                    "id": "PET",
                                    "petInfo": "{\"type\":\"ENDERMAN\",\"active\":false,\"exp\":1.6921923468420234E7,\"tier\":\"LEGENDARY\",\"hideInfo\":false,\"heldItem\":\"CROCHET_TIGER_PLUSHIE\",\"candyUsed\":0,\"uuid\":\"52337fc3-e6e0-4f5c-887b-116b094afdab\",\"uniqueId\":\"d3118c90-a752-4851-bcfb-46b98e17214b\",\"hideRightClick\":false,\"noMove\":false,\"extraData\":{},\"petSoulbound\":false}",
                                    "timestamp": 1782758801064,
                                    "uuid": "52337fc3-e6e0-4f5c-887b-116b094afdab"
                                },
                                "minecraft:custom_name": {
                                    "extra": [
                                        {
                                            "color": "gray",
                                            "text": "[Lvl 94] "
                                        },
                                        {
                                            "color": "gold",
                                            "text": "Enderman"
                                        }
                                    ],
                                    "italic": false,
                                    "text": ""
                                },
                                "minecraft:lore": [
                                    {
                                        "color": "dark_gray",
                                        "italic": false,
                                        "text": "Combat Pet"
                                    },
                                    "",
                                    {
                                        "color": "yellow",
                                        "italic": false,
                                        "text": "Right-click to add this pet to your pet menu!"
                                    },
                                    "",
                                    {
                                        "color": "dark_gray",
                                        "italic": false,
                                        "text": "Can be upgraded at Kat in The Hub!"
                                    },
                                    {
                                        "bold": true,
                                        "color": "gold",
                                        "italic": false,
                                        "text": "LEGENDARY"
                                    }
                                ]
                            }
                        }
                    ]
                }
                """).ToList();

        data.Should().HaveCount(1);
        var item = data[0];
        item.Should().NotBeNull();
        item.Tag.Should().Be("PET_ENDERMAN");
        item.ItemName.Should().Be("§7[Lvl 94] §6Enderman");
        item.Tier.Should().Be(Tier.LEGENDARY);
        item.Uuid.Should().Be("52337fc3-e6e0-4f5c-887b-116b094afdab");
        item.FlatenedNBT["heldItem"].Should().Be("CROCHET_TIGER_PLUSHIE");
    }

    [Test]
    public void ParseEnchantedBookToEnchantmentTag()
    {
        var parser = new InventoryParser();
        var data = parser.Parse("""
                {
                    "slots": [
                        {
                            "count": 1,
                            "displayName": "Enchanted Book",
                            "metadata": 0,
                            "name": "minecraft:enchanted_book",
                            "nbt": {
                                "minecraft:custom_data": {
                                    "enchantments": {
                                        "strong_mana": 5
                                    },
                                    "id": "ENCHANTED_BOOK",
                                    "timestamp": 1774665842008,
                                    "uuid": "43bc81c8-839c-4202-8c5f-0cf1ed0909cb"
                                },
                                "minecraft:custom_name": {
                                    "extra": [
                                        {
                                            "color": "green",
                                            "text": "Enchanted Book"
                                        }
                                    ],
                                    "italic": false,
                                    "text": ""
                                },
                                "minecraft:lore": [
                                    {
                                        "extra": [
                                            {
                                                "color": "gray",
                                                "text": "Strong Mana V"
                                            }
                                        ],
                                        "italic": false,
                                        "text": ""
                                    },
                                    {
                                        "extra": [
                                            {
                                                "bold": true,
                                                "color": "green",
                                                "text": "UNCOMMON"
                                            }
                                        ],
                                        "italic": false,
                                        "text": ""
                                    }
                                ]
                            },
                            "slot": 9,
                            "tag": "ENCHANTED_BOOK",
                            "type": 1244
                        }
                    ]
                }
                """).ToList();

        data.Should().HaveCount(1);
        var item = data[0];
        item.Should().NotBeNull();
        item.Tag.Should().Be("ENCHANTMENT_STRONG_MANA_5");
        item.Enchantments.Should().HaveCount(1);
        item.Enchantments.First().Type.Should().Be(Enchantment.EnchantmentType.strong_mana);
        item.Enchantments.First().Level.Should().Be(5);
    }

    [Test]
    public void ParseNewMineflayerItemFormat()
    {
        var parser = new InventoryParser();
        var json = """
        {"_events":{},"_eventsCount":0,"id":0,"type":"minecraft:inventory","title":"Inventory","slots":[null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,{"type":1235,"count":1,"metadata":0,"nbt":null,"components":[{"type":"tooltip_display","data":{"hideTooltip":false,"hiddenComponents":[62,97,44,67,16,4,53,70,54,49,58,42]}},{"type":"custom_name","data":{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"[Lvl 100] "}},{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"Griffin"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}},{"type":"lore","data":[{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_gray"},"text":{"type":"string","value":"Combat Pet"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Strength: "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"+80"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Crit Chance: "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"+10%"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Crit Damage: "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"+50%"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Attack Speed: "}},{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"+25%"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Speed: "}},{"color":{"type":"string","value":"white"},"text":{"type":"string","value":"+20"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"Odyssey"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"10 "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"types of "}},{"color":{"type":"string","value":"dark_green"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"dark_green"},"text":{"type":"string","value":"✿ Mythological "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"mobs can"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"spawn from "}},{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"Griffin Burrows"}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":". Their"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"stats scale with your Griffin’s"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"rarity."}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"Sacred Strength"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Gain "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"+15% ❁ Strength "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"when above"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"85% "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"❤ Health"}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"."}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"King of Kings"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Grants "}},{"color":{"type":"string","value":"aqua"},"text":{"type":"string","value":"+20✯ Magic Find "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"on "}},{"color":{"type":"string","value":"dark_green"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"dark_green"},"text":{"type":"string","value":"✿"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_green"},"text":{"type":"string","value":"Mythological "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"mobs."}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"Held Item: "}},{"color":{"type":"string","value":"green"},"text":{"type":"string","value":"Spooky Cupcake"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Grants "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"+30❁ Strength"}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":" and "}},{"color":{"type":"string","value":"white"},"text":{"type":"string","value":"+20✦"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"white"},"text":{"type":"string","value":"Speed"}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"."}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"aqua"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"MAX LEVEL"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_gray"},"text":{"type":"string","value":"▸ 67,416,060 XP"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"Right-click to add this pet to your pet menu!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"dark_gray"},"text":{"type":"string","value":"Can be upgraded at Kat in The Hub!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"LEGENDARY"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}]},{"type":"profile","data":{"type":"partial","uuid":"fa96ef2a-f697-3c9c-9a83-1d09dc9ad20e","properties":[{"name":"textures","value":"ewogICJ0aW1lc3RhbXAiIDogMTU5OTgxMDM3NTkzMywKICAicHJvZmlsZUlkIiA6ICI5YzQ5YjU0YjFjZjU0NjZjYjRhNzA4M2JmZGQ4MDIxNSIsCiAgInByb2ZpbGVOYW1lIiA6ICI5U0kiLAogICJzaWduYXR1cmVSZXF1aXJlZCIgOiB0cnVlLAogICJ0ZXh0dXJlcyIgOiB7CiAgICAiU0tJTiIgOiB7CiAgICAgICJ1cmwiIDogImh0dHA6Ly90ZXh0dXJlcy5taW5lY3JhZnQubmV0L3RleHR1cmUvNGMyN2UzY2I1MmE2NDk2OGU2MGM4NjFlZjFhYjg0ZTBhMGNiNWYwN2JlMTAzYWM3OGRhNjc3NjE3MzFmMDBjOCIKICAgIH0KICB9Cn0=","signature":"ZUYe3INMx4AW4QsduE/5TRxLJ3A0W9xHEPLXVGmFzQb5IHHdI00LIMUj5daqb9mG0WdMjwlw4CXee9hLjAOTcWSUcl7eC136rmiSjEWQGBhbKaxwwWhCJR0CdE1mjqD0KHvRed4efUfVYtoYf9sc0DOSyuOtD5Pu9f1Aklezh2Y6drbVxIwl7UR69fW3qu9s+VbvqbkrpwUZDqeNZ2hp64oPS4an/cScyjUPz4EQqYUP/Mi/Cf8W2x1J27UMdHfJ3iwyb4UJejfisWZrY5freGdv9Yhp3nneBW1NG1//aj0dUOxEr+H81WWClvomSTUIZuiWITC/2VhMUdocgxYncftL9U5XpgXp89JGCsb67ljxoHVUUVrie3McE7slbhn5JlGL1tTHzeeQYrvYAHmbTSTsC+jk+tloKpJlcRhIQlI4U2UYez1i7JIf5+U/DZ+qa7z68zk0no8tvoKHG7asTMrfk4oZ2Ptc7RM+LvJ3VEItpVpihHjMLBlCqZDCDgz/YspsodnfatFIXzQkaKz6ZfVvamglNlnMKXr68WLuX6U85RMzrhU/391H5YmUfkYhTCKhaKL01v+e/5HybE7M4ZpemIicbu3eTaY5NVZWRSstenizVnVIZCsC4jm1vXrm92CUABfLuHMcYWTEHjPBXfMfxov/X5ySh7ezAXL0Uhk="}],"skinPatch":{}}},{"type":"custom_data","data":{"type":"compound","value":{"petInfo":{"type":"string","value":"{\"type\":\"GRIFFIN\",\"active\":false,\"exp\":6.741606018659903E7,\"tier\":\"LEGENDARY\",\"hideInfo\":false,\"heldItem\":\"PET_ITEM_SPOOKY_CUPCAKE\",\"heldItemUuid\":\"616cd427-65d6-42ee-a85b-520394e83870\",\"candyUsed\":0,\"uuid\":\"5d3b2a33-b1ff-46c8-8009-20e0be6999a6\",\"uniqueId\":\"c6e7d7fe-11e0-47ce-a060-209df0ea8842\",\"hideRightClick\":false,\"noMove\":false,\"extraData\":{},\"petSoulbound\":false}"},"id":{"type":"string","value":"PET"},"uuid":{"type":"string","value":"5d3b2a33-b1ff-46c8-8009-20e0be6999a6"},"timestamp":{"type":"long","value":[414,-957355829]}}}}],"removedComponents":[],"componentMap":{},"stackId":null,"name":"player_head","displayName":"Player Head","stackSize":64,"slot":19},null,{"type":1235,"count":1,"metadata":0,"nbt":null,"components":[{"type":"tooltip_display","data":{"hideTooltip":false,"hiddenComponents":[62,97,44,67,16,4,53,70,54,49,58,42]}},{"type":"custom_name","data":{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"green"},"text":{"type":"string","value":"Abiphone X Red"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}},{"type":"lore","data":[{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"A device that can be used to contact"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"people! Click NPCs to add them to"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"your contacts!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Features:"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"text":{"type":"string","value":" "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Maximum Contacts: "}},{"color":{"type":"string","value":"aqua"},"text":{"type":"string","value":"7 "}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"text":{"type":"string","value":" "}},{"color":{"type":"string","value":"green"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"Contacts Directory"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"Right-click to open!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"green"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"UNCOMMON"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}]},{"type":"profile","data":{"type":"partial","uuid":"654c7ed7-8f8a-3659-a985-43b63dd29503","properties":[{"name":"textures","value":"ewogICJ0aW1lc3RhbXAiIDogMTc1NTcwNTI3NTE1NSwKICAicHJvZmlsZUlkIiA6ICJhODEzMzJmNjA5MmE0NDQ1Yjk1YzVhZGFjNDA2OTQzYiIsCiAgInByb2ZpbGVOYW1lIiA6ICJTbmFwcHliM2FzdCIsCiAgInNpZ25hdHVyZVJlcXVpcmVkIiA6IHRydWUsCiAgInRleHR1cmVzIiA6IHsKICAgICJTS0lOIiA6IHsKICAgICAgInVybCIgOiAiaHR0cDovL3RleHR1cmVzLm1pbmVjcmFmdC5uZXQvdGV4dHVyZS9kMjU2NTg0ZWVhNjgyNTE1MGE0MTVmYzE4ZGFhNTMyYjkxMzljY2E0NTRmOTY5ZjIyMTAzNTVkZTEwYTE1YTQiLAogICAgICAibWV0YWRhdGEiIDogewogICAgICAgICJtb2RlbCIgOiAic2xpbSIKICAgICAgfQogICAgfQogIH0KfQ==","signature":"wdx6h0NyDu32QDRJsDz68M1VpQMeJDhtTbGyr6zhB+SmgPIbCq22aXLSmnPZmzGYgEXfSnFqG8uuSK8Pez+gHqVmt8C0F0R2lCe8HZ7ieTtNO2uZw7vfrYZYcEqCSGHz5RUywhEqAq3Byoit4RFn7aBx/N+Oi+IzDfXdLUWKooyp3hpk/6AsvFoFeudSX8JcG9eOUuP5zJV9ZbZSEzlvlpHBoCkMBpO+d1drra/qmJZ2BT0mai1NI43FqR33CqEBDcbCUf04DTtJqMZIDiwE0NFIX/4CAmCvksUOzEIvtZ87B8Oc4dA/kBSpoZZcrs9SHBUjXt4fy8sJ5ZUn80UMTDN7FJYGf5881TGNBmkWogao00uu6JRzc94TkDoEqW2jXyFWGaqaRdxwvVy8F8+xUlTE/S+elQL9rsME5BSDvpWJjKOHkL+XJqLHCF4ehJSpZYsSazLAcIz+8++9dDRL/s41r0PV9JaLl+mdX/Qa1DjBVKinRlgBWV17hTB6eQ84YI93+2ygerOhgg8/oo98wbC1PqOuA+Zz9U6qD8ZvMJ8dCEb2VimHM0nS6k/Ayl7fBTPbUNOKU0BNjtZX0tggevud0bpH2bTkGSobFewrL6oSouxKyOKUVE+OFWpYNeBeDhN+JTokYopLW19XOKAjWTugDWYTKoF+ErUl+6m5Nxg="}],"skinPatch":{}}},{"type":"custom_data","data":{"type":"compound","value":{"id":{"type":"string","value":"ABIPHONE_X_PLUS"},"uuid":{"type":"string","value":"d50ec9bc-54de-4c14-84c8-c4d09458f246"},"timestamp":{"type":"long","value":[412,895699166]}}}}],"removedComponents":[],"componentMap":{},"stackId":null,"name":"player_head","displayName":"Player Head","stackSize":64,"slot":21},null,null,null,{"type":936,"count":1,"metadata":0,"nbt":{"type":"compound","name":"","value":{"Damage":{"type":"int","value":0}}},"components":[{"type":"tooltip_display","data":{"hideTooltip":false,"hiddenComponents":[62,97,44,67,16,4,53,70,54,49,58,42]}},{"type":"enchantment_glint_override","data":true},{"type":"unbreakable"},{"type":"custom_name","data":{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_purple"},"text":{"type":"string","value":"Legendary Void Sword"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}},{"type":"lore","data":[{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Damage: "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"+125"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Strength: "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"+18 "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"(+18)"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Crit Chance: "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"+12% "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"(+12%)"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Crit Damage: "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"+22% "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"(+22%)"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Attack Speed: "}},{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"+7% "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"(+7%)"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Intelligence: "}},{"color":{"type":"string","value":"aqua"},"text":{"type":"string","value":"+18 "}},{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"(+18)"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Critical V"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"Experience III"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"Fire Aspect II"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"Knockback II"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"Looting III"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"blue"},"text":{"type":"string","value":"Sharpness V"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"Some of your enchantments require"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"a higher Enchanting level!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Gain "}},{"color":{"type":"string","value":"red"},"text":{"type":"string","value":"+20❁ Strength "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"per piece of"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"dark_purple"},"text":{"type":"string","value":"Ender Armor "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"worn."}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Current Bonus: "}},{"color":{"type":"string","value":"green"},"text":{"type":"string","value":"0"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_gray"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"* "}},{"color":{"type":"string","value":"dark_gray"},"text":{"type":"string","value":"Co-op Soulbound "}},{"color":{"type":"string","value":"dark_gray"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"*"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_purple"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"EPIC SWORD"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}]},{"type":"custom_data","data":{"type":"compound","value":{"modifier":{"type":"string","value":"legendary"},"id":{"type":"string","value":"VOID_SWORD"},"enchantments":{"type":"compound","value":{"critical":{"type":"int","value":5},"looting":{"type":"int","value":3},"sharpness":{"type":"int","value":5},"knockback":{"type":"int","value":2},"fire_aspect":{"type":"int","value":2},"experience":{"type":"int","value":3}}},"uuid":{"type":"string","value":"6ecf668d-a883-4eec-92f1-c4007090059f"},"timestamp":{"type":"long","value":[397,-1957124524]}}}}],"removedComponents":[],"componentMap":{},"stackId":null,"name":"diamond_sword","displayName":"Diamond Sword","stackSize":1,"maxDurability":1561,"slot":25},null,null,null,null,null,null,null,null,null,null,{"type":339,"count":27,"metadata":0,"nbt":null,"components":[{"type":"tooltip_display","data":{"hideTooltip":false,"hiddenComponents":[62,97,44,67,16,4,53,70,54,49,58,42]}},{"type":"custom_name","data":{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"white"},"text":{"type":"string","value":"Snow Block"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}},{"type":"lore","data":[{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"white"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"COMMON"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}]},{"type":"custom_data","data":{"type":"compound","value":{"id":{"type":"string","value":"SNOW_BLOCK"}}}}],"removedComponents":[],"componentMap":{},"stackId":null,"name":"snow_block","displayName":"Snow Block","stackSize":64,"slot":36},null,null,{"type":1235,"count":1,"metadata":0,"nbt":null,"components":[{"type":"tooltip_display","data":{"hideTooltip":false,"hiddenComponents":[62,97,44,67,16,4,53,70,54,49,58,42]}},{"type":"custom_name","data":{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"Quick Claw"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}},{"type":"lore","data":[{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_gray"},"text":{"type":"string","value":"Consumed on use"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"green"},"text":{"type":"string","value":"Pet Items "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"can boost pets in many"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"powerful ways! A pet can only hold"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"one "}},{"color":{"type":"string","value":"green"},"text":{"type":"string","value":"Pet Item"}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":", but you can "}},{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"swap "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"it at"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"any time!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"dark_gray"},"text":{"type":"string","value":"The pet must be visible to apply the item!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Every 2 pet levels, you gain "}},{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"+1⸕"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"Mining Speed "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"and "}},{"color":{"type":"string","value":"gold"},"text":{"type":"string","value":"+1☘ Mining Fortune"}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"."}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":""}},{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"Right-click on your summoned pet to"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"give it this item!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gold"},"bold":{"type":"byte","value":1},"text":{"type":"string","value":"LEGENDARY PET ITEM"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}]},{"type":"profile","data":{"type":"partial","uuid":"490f6025-7f41-322a-9c7a-9d1cf91ee0ae","properties":[{"name":"textures","value":"ewogICJ0aW1lc3RhbXAiIDogMTcyMDA0MjcwODkzOCwKICAicHJvZmlsZUlkIiA6ICI4ZjE5NjJmYzE4NzY0MDU3ODYxMmIxMzNjODE4YmY5OSIsCiAgInByb2ZpbGVOYW1lIiA6ICJOaW9uXzkiLAogICJzaWduYXR1cmVSZXF1aXJlZCIgOiB0cnVlLAogICJ0ZXh0dXJlcyIgOiB7CiAgICAiU0tJTiIgOiB7CiAgICAgICJ1cmwiIDogImh0dHA6Ly90ZXh0dXJlcy5taW5lY3JhZnQubmV0L3RleHR1cmUvNjMxMmE1YTEyZWNiMjRkNjg1MmRiMzg4ZTZhMzQ3MjFjYzY3ZjUyMmNjZGU3ZTgyNGI5Zjc1ZTk1MDM2YWM5MyIKICAgIH0KICB9Cn0=","signature":"jVSRfNjGMmW92rIu173wUWePEWXSzx7y+6AMDGuUjKG8mXN2DLfINzrYuABovc7v3+dG//duti8X3KhqUQiMIKjK+EPUryU3jSZ8pb4EX1SLBTDX6KpxWDXsEteq6XobfmdOQ1bW5DIvvtsnOLQ7kMiONRZjqGYkfnekju9dPX9RlC5D0XVu5whRCKlZ7Vm1vpdPxFWwDpQD4U9okcJKiCnedMRN8IzY9898d5Vh+acTLW3Y3zlQs3YpHJ9+14gozIWrEfp51EVhj1cXBOFwDjAlJwgbtvvujSIqLe0qKpi7idikO4oRg8wPOC93PGWBMYJ8vNTirXTkq/mkSkc2GnV7ABM4U6EgckpugVEECDjT5klJCf+7y749roy7nyMBaCNqYs409O8JWTD42DHZQ+YkESPdfJCNkG6VlDDrCOSe/trYLGzBz7C4ZbM2owWUzNdR/+5oQLch+/4sjim8deSNIEy4mQWc8DsSu2vcTDMqTWNMB8uz7JXD+QeDDFnR4BdQNRyN/Zq9VSaTim2qZh8dhr2nW4Tn09aX8eR1L3jbl86BaaV47i3bkhzWgc69lgJWqsS/s4GsEMtpU2XvIkYrxuIW2Ifv3X9s0UGHFaelP6jPZqqD/9gmHtsjBwYSThRIZ0638fPMxUDEfOYorueXaUg7jBxvabT3EX5D/SU="}],"skinPatch":{}}},{"type":"custom_data","data":{"type":"compound","value":{"id":{"type":"string","value":"PET_ITEM_QUICK_CLAW"},"uuid":{"type":"string","value":"9870556a-0672-4e44-8fd7-506325e978fd"},"timestamp":{"type":"long","value":[414,-1017152871]}}}}],"removedComponents":[],"componentMap":{},"stackId":null,"name":"player_head","displayName":"Player Head","stackSize":64,"slot":39},null,null,null,null,{"type":1240,"count":1,"metadata":0,"nbt":null,"components":[{"type":"tooltip_display","data":{"hideTooltip":false,"hiddenComponents":[62,97,44,67,16,4,53,70,54,49,58,42]}},{"type":"custom_name","data":{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"green"},"text":{"type":"string","value":"SkyBlock Menu "}},{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"(Click)"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}},{"type":"lore","data":[{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"View all of your SkyBlock progress,"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"including your Skills, Collections,"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"gray"},"text":{"type":"string","value":"Recipes, and more!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}},{"type":"compound","value":{"extra":{"type":"list","value":{"type":"compound","value":[{"color":{"type":"string","value":"yellow"},"text":{"type":"string","value":"Click to open!"}}]}},"text":{"type":"string","value":""},"italic":{"type":"byte","value":0}}}]},{"type":"custom_data","data":{"type":"compound","value":{"id":{"type":"string","value":"SKYBLOCK_MENU"}}}}],"removedComponents":[],"componentMap":{},"stackId":null,"name":"nether_star","displayName":"Nether Star","stackSize":64,"slot":44},null],"inventoryStart":9,"inventoryEnd":45,"hotbarStart":36,"craftingResultSlot":0,"requiresConfirmation":true,"selectedItem":null}
        """;

        var data = parser.Parse(json).ToList();

        data.Should().NotBeNull();

        var sword = data.FirstOrDefault(i => i?.Tag == "VOID_SWORD");
        sword.Should().NotBeNull();

        sword.Tag.Should().Be("VOID_SWORD");
        sword.ItemName.Should().Contain("Legendary Void Sword");
        sword.Context["lore"].Should().Contain("Damage: §c+125");
        sword.Context["lore"].Should().Contain("§l§5EPIC SWORD");
        
        sword.Count.Should().Be(1);
        sword.Tier.Should().Be(Tier.EPIC);
        sword.Reforge.Should().Be(ItemReferences.Reforge.Legendary);
        sword.Uuid.Should().Be("6ecf668d-a883-4eec-92f1-c4007090059f");

        sword.Enchantments.Should().HaveCount(6);
        sword.Enchantments.Should().ContainSingle(e => e.Type == Enchantment.EnchantmentType.critical && e.Level == 5);
        sword.Enchantments.Should().ContainSingle(e => e.Type == Enchantment.EnchantmentType.looting && e.Level == 3);
        sword.Enchantments.Should().ContainSingle(e => e.Type == Enchantment.EnchantmentType.sharpness && e.Level == 5);
        
        sword.FlatenedNBT.Should().ContainKey("uuid");
        sword.FlatenedNBT["uuid"].Should().Be("6ecf668d-a883-4eec-92f1-c4007090059f");

        var snowBlock = data.FirstOrDefault(i => i?.Tag == "SNOW_BLOCK");
        snowBlock.Should().NotBeNull();
        snowBlock.ItemName.Should().Contain("Snow Block");
        snowBlock.Count.Should().Be(27);
        snowBlock.Tier.Should().Be(Tier.COMMON);

        var pet = data.FirstOrDefault(i => i?.Tag == "PET_GRIFFIN");
        pet.Should().NotBeNull();
        pet.ItemName.Should().Contain("Griffin");
        pet.Tier.Should().Be(Tier.LEGENDARY);
        pet.Uuid.Should().Be("5d3b2a33-b1ff-46c8-8009-20e0be6999a6");

        var claw = data.FirstOrDefault(i => i?.Tag == "PET_ITEM_QUICK_CLAW");
        claw.Should().NotBeNull();
        claw.ItemName.Should().Contain("Quick Claw");
        claw.Tier.Should().Be(Tier.LEGENDARY);

        var menu = data.FirstOrDefault(i => i?.Tag == "SKYBLOCK_MENU");
        menu.Should().NotBeNull();
        menu.ItemName.Should().Contain("SkyBlock Menu");

        var abiphone = data.FirstOrDefault(i => i?.Tag == "ABIPHONE_X_PLUS");
        abiphone.Should().NotBeNull();
        abiphone.ItemName.Should().Contain("Abiphone X Red");
        abiphone.Uuid.Should().Be("d50ec9bc-54de-4c14-84c8-c4d09458f246");
    }
}
