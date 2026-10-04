namespace F1M24VoiceManager.Core;

/// <summary>
/// Staff IDs that ship with recorded radio dialogue in F1 Manager 2024.
/// Source: F1M24 Voice Manager 1.1.0 catalogue (docs/voice-manager-analysis.md).
/// </summary>
public static class VoiceCatalog
{
    public sealed record Donor(int StaffId, string Name, VoiceRole Role);

    public static IReadOnlyList<Donor> Drivers { get; } = new (int Id, string Name)[]
    {
        (1, "Lewis Hamilton"), (2, "Charles Leclerc"), (3, "Alexander Albon"), (8, "Valtteri Bottas"),
        (9, "Sebastian Vettel"), (10, "Max Verstappen"), (11, "Carlos Sainz"), (12, "Lando Norris"),
        (13, "Daniel Ricciardo"), (14, "Esteban Ocon"), (15, "Pierre Gasly"), (17, "Sergio Perez"),
        (18, "Lance Stroll"), (20, "Antonio Giovinazzi"), (22, "Stoffel Vandoorne"), (23, "George Russell"),
        (74, "Mick Schumacher"), (77, "Fernando Alonso"), (78, "Roy Nissany"), (80, "Pietro Fittipaldi"),
        (81, "Yuki Tsunoda"), (83, "Nico Hulkenberg"), (87, "Theo Pourchaire"), (88, "Ralph Boschung"),
        (91, "Jehan Daruvala"), (95, "Liam Lawson"), (99, "Richard Verschoor"), (102, "Oscar Piastri"),
        (105, "Guanyu Zhou"), (106, "Felipe Drugovich"), (107, "Frederik Vesti"), (109, "Juan Manuel Correa"),
        (110, "Amaury Cordeel"), (114, "Ido Cohen"), (115, "Kaylen Frederick"), (116, "Logan Sargeant"),
        (117, "Enzo Fittipaldi"), (119, "Roman Stanek"), (120, "Ayumu Iwasa"), (121, "Jak Crawford"),
        (123, "Rafael Villagomez"), (127, "Victor Martins"), (128, "Caio Collet"), (130, "Dennis Hauger"),
        (132, "Arthur Leclerc"), (133, "Clement Novalak"), (135, "Jack Doohan"), (140, "Jake Hughes"),
        (141, "Cem Bolukbasi"), (142, "Oliver Bearman"), (143, "Gregoire Saucy"), (144, "Isack Hadjar"),
        (242, "Zane Maloney"), (243, "Francesco Pizzi"), (244, "Hunter Yeany"), (245, "Pepe Marti"),
        (246, "William Alatalo"), (247, "Zak O'Sullivan"), (248, "Franco Colapinto"), (249, "Reece Ushijima"),
        (250, "David Vidales"), (251, "Ayrton Simmons"), (252, "Kush Maini"), (253, "Brad Benavides"),
        (255, "Kevin Magnussen"), (264, "Sebastien Buemi"), (265, "Enzo Trulli"), (267, "Nazim Azman"),
        (268, "Oliver Rasmussen"), (269, "Federico Malvestiti"), (270, "David Schumacher"), (272, "Jonny Edgar"),
        (273, "Filip Ugran"), (274, "Zdenek Chovanec"), (275, "Lirim Zendeli"), (276, "David Beckmann"),
        (277, "Roberto Merhi"), (278, "Niko Kari"), (279, "Gabriel Bortoleto"), (280, "Gabriele Mini"),
        (281, "Paul Aron"), (282, "Dino Beganovic"), (283, "Mari Boya"), (284, "Christian Mansell"),
        (285, "Pato O'Ward"), (286, "Nikola Tsolov"), (287, "Tommy Smith"), (288, "Oliver Goethe"),
        (289, "Leonardo Fornaroli"), (300, "Oliver Gray"), (301, "Sebastian Montoya"), (302, "Hugh Barter"),
        (303, "Alejandro Garcia"), (304, "Nikita Bedrin"), (305, "Taylor Barnard"), (306, "Sophia Florsch"),
        (307, "Roberto Faria"), (308, "Piotr Wisnicki"), (322, "Luke Browning"), (371, "Mckenzy Cresswell"),
        (376, "Andrea Kimi Antonelli"), (377, "Ritomo Miyata"), (401, "Carrie Schreiner"), (402, "Chloe Chambers"),
        (405, "Abbi Pulling"), (409, "Tina Hausmann"), (410, "Bianca Bustamante"), (413, "Robert Shwartzman"),
    }.Select(d => new Donor(d.Id, d.Name, VoiceRole.Driver)).ToArray();

    public static IReadOnlyList<Donor> Engineers { get; } = new (int Id, string Name)[]
    {
        (6, "Xavier Marcos Padros"), (7, "Riccardo Adami"), (47, "Tom Stallard"), (48, "William Joseph"),
        (49, "Hugh Bird"), (50, "Gianpiero Lambiase"), (51, "Peter Bonnington"), (54, "Josh Peckett"),
        (55, "James Urwin"), (56, "Gaetan Jego"), (58, "Gary Gannon"), (59, "Pierre Hamelin"),
        (60, "Mattia Spini"), (61, "Alex Chan"), (63, "Chris Cronin"), (64, "Ben Michell"),
        (148, "Stuart King"), (149, "Mathew Ogle"), (150, "Antoine Voyet"), (151, "Romain Goasguen"),
        (155, "Pedro Matos"), (158, "Geoff Spear"), (159, "Pau Rivera"), (160, "Alan Queille"),
        (161, "Jan Sumann"), (174, "Jose Fontestad"), (176, "Joao Ginete"), (320, "Marcus Dudley"),
        (321, "Mark Slade"), (393, "Andrea Benisi"), (415, "John Howard"),
    }.Select(d => new Donor(d.Id, d.Name, VoiceRole.Engineer)).ToArray();

    private static readonly HashSet<int> DriverIds = Drivers.Select(d => d.StaffId).ToHashSet();
    private static readonly HashSet<int> EngineerIds = Engineers.Select(d => d.StaffId).ToHashSet();

    public static IReadOnlyList<Donor> For(VoiceRole role) => role == VoiceRole.Driver ? Drivers : Engineers;

    public static bool HasRecordedVoice(VoiceRole role, int staffId) =>
        (role == VoiceRole.Driver ? DriverIds : EngineerIds).Contains(staffId);
}
