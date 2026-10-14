namespace FitEdu.Application.DTOs;

/// <summary>
/// Static seed content for blog articles: four fitness articles, each in English (En) and Greek (El).
/// Covers are site-root paths under wwwroot/images/blog; tags are English keywords that BlogTagLabels shows in the page language.
/// </summary>
public static class BlogSeedData
{
    public static IReadOnlyList<SaveBlogArticleRequest> SeedArticles { get; } =
    [
        new SaveBlogArticleRequest(
            TitleEn: "Progressive Overload, Explained: How to Keep Getting Stronger",
            TitleEl: "Η προοδευτική υπερφόρτωση με απλά λόγια: Πώς να συνεχίζεις να δυναμώνεις",
            Slug: "progressive-overload-explained",
            SummaryEn: "Your body adapts to the work you give it, then stops adapting once that work feels easy. Progressive overload is the simple, structured way to ask for a little more each time, without rushing into injury.",
            SummaryEl: "Το σώμα σου προσαρμόζεται στη δουλειά που του δίνεις και σταματά να προσαρμόζεται όταν αυτή γίνει εύκολη. Η προοδευτική υπερφόρτωση είναι ο απλός, οργανωμένος τρόπος να ζητάς λίγο περισσότερο κάθε φορά, χωρίς να βιάζεσαι και να τραυματιστείς.",
            ContentEn: """
                <p>If you have trained for a few weeks, you have probably noticed that the workout which felt brutal on day one now feels manageable. That is not luck; it is adaptation. Muscles, tendons, bones and your nervous system respond to a training stress by becoming better able to handle it. The catch is that once a stress becomes routine, the reason to keep adapting fades. Progressive overload is the principle of gradually increasing the demand on your body so that progress continues.</p>
                <h2>What "overload" actually means</h2>
                <p>Overload does not mean training until you collapse. It means doing slightly more than your body is currently used to. For a beginner, that might be one extra repetition on a set of push-ups. For an experienced lifter, it might be two and a half kilos more on the squat after several weeks. The key word is <strong>progressive</strong>: the increases are small, planned and spread over time.</p>
                <h2>Ways to progress (it is not only about weight)</h2>
                <p>Adding load is the best-known option, but it is only one lever. You can make a session harder in several ways:</p>
                <ul>
                <li><strong>Load:</strong> lift a heavier weight for the same number of repetitions.</li>
                <li><strong>Repetitions:</strong> do more reps with the same weight before you increase it.</li>
                <li><strong>Sets:</strong> add a set to an exercise, which raises your total weekly volume.</li>
                <li><strong>Range of motion:</strong> squat a little deeper or lower each push-up all the way down.</li>
                <li><strong>Tempo and control:</strong> slow the lowering phase to three seconds so the muscle works for longer.</li>
                <li><strong>Exercise difficulty:</strong> move from an incline push-up to a floor push-up, or from a goblet squat to a barbell squat.</li>
                <li><strong>Rest:</strong> in conditioning work, shorten the rest between rounds.</li>
                </ul>
                <p>Change one lever at a time. If you add weight, sets and speed in the same week, you will not know what worked, and you raise the risk of doing too much too soon.</p>
                <h2>A simple method: double progression</h2>
                <p>One of the easiest systems for beginners and intermediates is called double progression. You pick a repetition range, for example 8 to 12. Start with a weight you can lift for 8 good reps on every set. Each session, try to add a rep or two. When you can complete 12 clean reps on all sets, increase the weight by the smallest step available, often 1 to 2.5 kg for upper-body lifts and 2.5 to 5 kg for lower-body lifts, and drop back to 8 reps. Then build up again.</p>
                <p>This method works because it gives you two ways to progress and a clear rule for when to move up. It also stops you from adding weight before your technique is ready for it.</p>
                <h2>How fast should you progress?</h2>
                <p>Expect quick gains in your first months, mostly because your nervous system is learning the movements. Beginners can often add a little weight or a few reps every week. As you gain experience, progress slows, and cycles of a month or longer become normal. That slowdown is a sign that you are advancing, not that you are failing.</p>
                <p>A practical guideline used by many coaches is to raise the load by roughly 2 to 10 percent once you can comfortably beat your target reps in two sessions in a row. When in doubt, choose the smaller jump.</p>
                <h2>Let effort, not ego, be your guide</h2>
                <p>A helpful tool is the Rating of Perceived Exertion (RPE), a 1 to 10 score for how hard a set felt. Most working sets for general strength should land around RPE 7 to 8, meaning you could have done two or three more reps with good form. You do not need to reach complete failure on every set to progress, and doing so makes recovery harder. Save near-maximal efforts for a few sets, and only on exercises you perform with confident technique.</p>
                <h2>Signs you are pushing too hard</h2>
                <ul>
                <li>Your form breaks down earlier in each set than it used to.</li>
                <li>Your performance drops for more than a week even though you sleep and eat normally.</li>
                <li>Joint pain (as opposed to normal muscle soreness) appears or gets worse.</li>
                <li>You feel unusually tired, irritable or unmotivated for days.</li>
                </ul>
                <p>If you notice several of these, hold your loads steady or reduce them for a week. Many programmes build in a lighter "deload" week every four to eight weeks for exactly this reason.</p>
                <h2>Keep a training log</h2>
                <p>You cannot improve what you do not track. Write down the exercise, weight, sets, reps and how hard it felt. Before each session, look at what you did last time and set one small target to beat. Over the months, that log becomes the clearest proof that the process is working.</p>
                <h2>The bottom line</h2>
                <p>Progressive overload is the engine behind every strength and muscle-building programme. Ask your body for a little more, give it time to recover, and repeat. Small, consistent steps add up to results that last.</p>
                <p><strong>Note:</strong> This article is general information, not medical advice — consult a professional before starting if you have a health condition.</p>
                """,
            ContentEl: """
                <p>Αν προπονείσαι εδώ και μερικές εβδομάδες, μάλλον έχεις προσέξει ότι η προπόνηση που σε εξόντωνε την πρώτη μέρα τώρα βγαίνει πιο άνετα. Δεν είναι τύχη· είναι προσαρμογή. Οι μύες, οι τένοντες, τα οστά και το νευρικό σου σύστημα απαντούν σε ένα προπονητικό ερέθισμα γινόμενα πιο ικανά να το αντέξουν. Το «πρόβλημα» είναι ότι, μόλις το ερέθισμα γίνει ρουτίνα, ο λόγος για περαιτέρω προσαρμογή εξασθενεί. Η προοδευτική υπερφόρτωση είναι η αρχή σύμφωνα με την οποία αυξάνεις σταδιακά την απαίτηση από το σώμα σου, ώστε η πρόοδος να συνεχίζεται.</p>
                <h2>Τι σημαίνει στην πράξη «υπερφόρτωση»</h2>
                <p>Υπερφόρτωση δεν σημαίνει να προπονείσαι μέχρι να καταρρεύσεις. Σημαίνει να κάνεις λίγο περισσότερα από όσα έχει συνηθίσει το σώμα σου. Για έναν αρχάριο, αυτό μπορεί να είναι μία επιπλέον επανάληψη σε ένα σετ push-ups. Για κάποιον με εμπειρία, μπορεί να είναι δυόμισι κιλά παραπάνω στο squat μετά από αρκετές εβδομάδες. Η λέξη-κλειδί είναι το <strong>προοδευτική</strong>: οι αυξήσεις είναι μικρές, προγραμματισμένες και μοιρασμένες στον χρόνο.</p>
                <h2>Τρόποι να προοδεύσεις (δεν είναι μόνο το βάρος)</h2>
                <p>Η προσθήκη βάρους είναι η πιο γνωστή επιλογή, αλλά είναι μόνο ένας από τους «μοχλούς». Μπορείς να κάνεις μια προπόνηση πιο απαιτητική με διάφορους τρόπους:</p>
                <ul>
                <li><strong>Φορτίο:</strong> σήκωσε μεγαλύτερο βάρος για τον ίδιο αριθμό επαναλήψεων.</li>
                <li><strong>Επαναλήψεις:</strong> κάνε περισσότερες επαναλήψεις με το ίδιο βάρος πριν το αυξήσεις.</li>
                <li><strong>Σετ:</strong> πρόσθεσε ένα σετ σε μια άσκηση, ώστε να ανέβει ο συνολικός εβδομαδιαίος όγκος.</li>
                <li><strong>Εύρος κίνησης:</strong> κατέβα λίγο πιο βαθιά στο squat ή φέρε κάθε push-up μέχρι κάτω.</li>
                <li><strong>Ρυθμός και έλεγχος:</strong> κάνε το κατέβασμα πιο αργό, στα τρία δευτερόλεπτα, ώστε ο μυς να δουλεύει περισσότερη ώρα.</li>
                <li><strong>Δυσκολία άσκησης:</strong> πέρνα από το push-up σε κλίση στο push-up στο πάτωμα ή από το goblet squat στο squat με μπάρα.</li>
                <li><strong>Διάλειμμα:</strong> στην προπόνηση φυσικής κατάστασης, μίκρυνε την ανάπαυση ανάμεσα στους γύρους.</li>
                </ul>
                <p>Άλλαζε έναν μοχλό τη φορά. Αν την ίδια εβδομάδα προσθέσεις βάρος, σετ και ταχύτητα, δεν θα ξέρεις τι έπιασε και αυξάνεις τον κίνδυνο να κάνεις πάρα πολλά πολύ νωρίς.</p>
                <h2>Μια απλή μέθοδος: διπλή προοδευτικότητα</h2>
                <p>Ένα από τα πιο εύκολα συστήματα για αρχάριους και ασκούμενους μεσαίου επιπέδου λέγεται διπλή προοδευτικότητα (double progression). Διαλέγεις ένα εύρος επαναλήψεων, για παράδειγμα 8 έως 12. Ξεκινάς με ένα βάρος που μπορείς να σηκώσεις για 8 καλές επαναλήψεις σε κάθε σετ. Σε κάθε προπόνηση προσπαθείς να προσθέσεις μία ή δύο επαναλήψεις. Όταν καταφέρεις 12 καθαρές επαναλήψεις σε όλα τα σετ, ανεβάζεις το βάρος κατά το μικρότερο διαθέσιμο βήμα, συνήθως 1 έως 2,5 κιλά στις ασκήσεις άνω κορμού και 2,5 έως 5 κιλά στις ασκήσεις κάτω άκρων, και επιστρέφεις στις 8 επαναλήψεις. Μετά χτίζεις ξανά από την αρχή.</p>
                <p>Η μέθοδος δουλεύει επειδή σου δίνει δύο τρόπους να προοδεύεις και έναν σαφή κανόνα για το πότε ανεβαίνεις. Επίσης σε εμποδίζει να βάλεις βάρος πριν είναι έτοιμη η τεχνική σου.</p>
                <h2>Πόσο γρήγορα πρέπει να προοδεύεις;</h2>
                <p>Στους πρώτους μήνες περίμενε γρήγορα κέρδη, κυρίως επειδή το νευρικό σου σύστημα μαθαίνει τις κινήσεις. Οι αρχάριοι συχνά μπορούν να προσθέτουν λίγο βάρος ή μερικές επαναλήψεις κάθε εβδομάδα. Όσο αποκτάς εμπειρία, η πρόοδος επιβραδύνεται και οι κύκλοι του ενός μήνα ή και μεγαλύτεροι γίνονται φυσιολογικοί. Αυτή η επιβράδυνση δείχνει ότι προχωράς, όχι ότι αποτυγχάνεις.</p>
                <p>Ένας πρακτικός κανόνας που χρησιμοποιούν πολλοί προπονητές είναι να αυξάνεις το φορτίο κατά 2 έως 10 τοις εκατό περίπου, μόλις ξεπερνάς άνετα τις επαναλήψεις-στόχο σε δύο συνεχόμενες προπονήσεις. Αν έχεις αμφιβολία, προτίμησε το μικρότερο άλμα.</p>
                <h2>Οδηγός σου η προσπάθεια, όχι ο εγωισμός</h2>
                <p>Ένα χρήσιμο εργαλείο είναι η κλίμακα αντιληπτής κόπωσης (RPE), μια βαθμολογία από το 1 έως το 10 για το πόσο δύσκολο ένιωσες ένα σετ. Τα περισσότερα σετ εργασίας για γενική δύναμη καλό είναι να βρίσκονται γύρω στο RPE 7 με 8, δηλαδή να μπορούσες να κάνεις ακόμη δύο ή τρεις επαναλήψεις με καλή τεχνική. Δεν χρειάζεται να φτάνεις σε πλήρη αποτυχία σε κάθε σετ για να προοδεύσεις, και αυτό δυσκολεύει την αποκατάσταση. Κράτα τις σχεδόν μέγιστες προσπάθειες για λίγα σετ, και μόνο σε ασκήσεις που εκτελείς με σιγουριά στην τεχνική.</p>
                <h2>Σημάδια ότι το παρακάνεις</h2>
                <ul>
                <li>Η τεχνική σου «σπάει» νωρίτερα μέσα στο σετ απ' ό,τι παλιότερα.</li>
                <li>Η απόδοσή σου πέφτει για πάνω από μία εβδομάδα, παρότι κοιμάσαι και τρως κανονικά.</li>
                <li>Εμφανίζεται ή χειροτερεύει πόνος στις αρθρώσεις (διαφορετικός από τον φυσιολογικό μυϊκό πόνο).</li>
                <li>Νιώθεις ασυνήθιστη κούραση, εκνευρισμό ή έλλειψη διάθεσης για μέρες.</li>
                </ul>
                <p>Αν παρατηρείς αρκετά από αυτά, κράτα τα φορτία σταθερά ή μείωσέ τα για μία εβδομάδα. Πολλά προγράμματα έχουν ενσωματωμένη μια πιο ελαφριά εβδομάδα αποφόρτισης (deload) κάθε τέσσερις έως οκτώ εβδομάδες ακριβώς γι' αυτόν τον λόγο.</p>
                <h2>Κράτα ημερολόγιο προπόνησης</h2>
                <p>Δεν μπορείς να βελτιώσεις κάτι που δεν καταγράφεις. Γράψε την άσκηση, το βάρος, τα σετ, τις επαναλήψεις και πόσο δύσκολο σου φάνηκε. Πριν από κάθε προπόνηση, δες τι έκανες την προηγούμενη φορά και βάλε έναν μικρό στόχο για να το ξεπεράσεις. Με τους μήνες, αυτό το ημερολόγιο γίνεται η πιο καθαρή απόδειξη ότι η διαδικασία δουλεύει.</p>
                <h2>Συμπέρασμα</h2>
                <p>Η προοδευτική υπερφόρτωση είναι η μηχανή πίσω από κάθε πρόγραμμα δύναμης και μυϊκής ανάπτυξης. Ζήτα από το σώμα σου λίγο περισσότερο, δώσ' του χρόνο να ανακάμψει και επανάλαβε. Τα μικρά, σταθερά βήματα αθροίζονται σε αποτελέσματα που διαρκούν.</p>
                <p><strong>Σημείωση:</strong> Το άρθρο αυτό παρέχει γενικές πληροφορίες και δεν αποτελεί ιατρική συμβουλή — συμβουλέψου επαγγελματία πριν ξεκινήσεις αν έχεις κάποια πάθηση.</p>
                """,
            CoverImageUrl: "/images/blog/progressive-overload-explained.webp",
            AuthorName: "FitEdu",
            Tags: ["Strength", "Beginners"],
            IsPublished: true
        ),

        new SaveBlogArticleRequest(
            TitleEn: "How Much Protein Do You Really Need?",
            TitleEl: "Πόση πρωτεΐνη χρειάζεσαι πραγματικά;",
            Slug: "how-much-protein-do-you-need",
            SummaryEn: "Protein supports muscle repair and growth, but you do not need endless shakes. Here is a practical, evidence-based daily range for active adults and simple ways to reach it with everyday food.",
            SummaryEl: "Η πρωτεΐνη στηρίζει την αποκατάσταση και την ανάπτυξη των μυών, αλλά δεν χρειάζεσαι ατελείωτα shakes. Δες ένα πρακτικό ημερήσιο εύρος, βασισμένο σε επιστημονικά δεδομένα, για δραστήριους ενήλικες και απλούς τρόπους να το καλύψεις με καθημερινό φαγητό.",
            ContentEn: """
                <p>Protein is the nutrient people in gyms talk about most, and also the one surrounded by the most confusion. Some people barely eat any; others believe they need a shake after every set. The truth sits in a sensible middle ground, and it is easier to reach than you might think.</p>
                <h2>Why protein matters for training</h2>
                <p>Protein is made of amino acids, the building blocks your body uses to repair and build tissue, including muscle. Training provides the signal to adapt; protein supplies the raw material. Without enough of it, you recover more slowly and build less muscle from the same work. Protein-rich meals also tend to keep you full for longer, which can make your appetite easier to manage.</p>
                <h2>The numbers: how much per day</h2>
                <p>The general recommendation for adults is about 0.8 grams of protein per kilogram of body weight per day. That figure is set to prevent deficiency in the general population, most of whom do little structured exercise. For adults who train regularly, sports nutrition organisations generally recommend more: roughly <strong>1.2 to 2.0 g per kg of body weight per day</strong>.</p>
                <ul>
                <li><strong>About 1.2–1.6 g/kg</strong> suits most people who do regular mixed training for health and fitness.</li>
                <li><strong>About 1.6–2.0 g/kg</strong> is a sensible target if you lift seriously to build muscle, or want to hold on to muscle while eating in a calorie deficit.</li>
                </ul>
                <p>For example, a 70 kg person who trains three or four times a week would aim for roughly 85 to 140 g of protein a day, depending on their goal. Going much higher is unlikely to add further muscle benefit for most people. Older adults often do well toward the upper end of the range, because muscle becomes less responsive to protein with age.</p>
                <h2>Spread it across the day</h2>
                <p>Your body makes better use of protein when it arrives in regular servings rather than in one huge meal. A practical approach is to include a protein source in each of three or four meals, aiming for roughly 20 to 40 g each time, or about 0.3 g per kg of body weight per meal.</p>
                <h2>Does timing around workouts matter?</h2>
                <p>A protein-rich meal in the few hours before or after training is a good habit, but the so-called "anabolic window" is far wider than the old 30-minute rule suggested. Your total daily intake matters much more than the exact minute you eat. The main exception is training on an empty stomach first thing in the morning: in that case, have a meal with protein soon afterwards.</p>
                <h2>Food first</h2>
                <p>You can meet your needs with ordinary meals. Approximate protein in some common foods:</p>
                <ul>
                <li>100 g cooked chicken breast: about 30 g</li>
                <li>100 g canned tuna, drained: about 25 g</li>
                <li>200 g plain strained Greek yoghurt (2% fat): about 20 g</li>
                <li>2 large eggs: about 12 g</li>
                <li>150 g cooked lentils or chickpeas: about 13 g</li>
                <li>100 g firm tofu: about 12–15 g</li>
                <li>30 g hard cheese such as graviera or cheddar: about 7 g</li>
                </ul>
                <p>If you follow a plant-based diet, you can reach the same targets by combining legumes, tofu, tempeh, soy milk, nuts, seeds and whole grains through the day. Because some plant proteins are lower in certain amino acids, aim for the upper half of the range and mix your sources.</p>
                <h2>What about protein powder?</h2>
                <p>A shake is simply convenient food, not a requirement. It can help on busy days or when your appetite is small, but it works no better than a meal with the same amount of protein. If you use one, choose a product that has been tested by an independent third party, and treat it as a top-up rather than the foundation of your diet.</p>
                <h2>A simple way to start</h2>
                <ol>
                <li>Multiply your body weight in kilograms by 1.4 to get a starting daily target.</li>
                <li>Divide that number by the number of meals you eat to get a goal per meal.</li>
                <li>Build each meal around at least a palm-sized portion of a protein food, then add vegetables, carbohydrates and some healthy fats.</li>
                <li>Note what you eat for three or four days to check that you are close, then adjust.</li>
                </ol>
                <h2>The bottom line</h2>
                <p>If you train regularly, aim for about 1.2 to 2.0 g of protein per kilogram of body weight per day, spread across your meals and taken mostly from whole foods. Hit that consistently, keep training progressively and sleep well, and your body will have what it needs to adapt.</p>
                <p><strong>Note:</strong> This article is general information, not medical advice — if you have kidney disease or another health condition, or are pregnant, check with a doctor or registered dietitian before changing your diet.</p>
                """,
            ContentEl: """
                <p>Η πρωτεΐνη είναι το θρεπτικό συστατικό για το οποίο μιλάνε περισσότερο στα γυμναστήρια, και ταυτόχρονα αυτό που περιβάλλεται από τη μεγαλύτερη σύγχυση. Κάποιοι σχεδόν δεν τρώνε καθόλου· άλλοι πιστεύουν ότι χρειάζονται ένα shake μετά από κάθε σετ. Η αλήθεια βρίσκεται σε μια λογική μέση οδό, και είναι πιο εύκολο να τη φτάσεις απ' ό,τι νομίζεις.</p>
                <h2>Γιατί η πρωτεΐνη μετράει στην προπόνηση</h2>
                <p>Η πρωτεΐνη αποτελείται από αμινοξέα, τα δομικά υλικά που χρησιμοποιεί το σώμα σου για να επισκευάσει και να χτίσει ιστούς, μεταξύ των οποίων και τους μύες. Η προπόνηση δίνει το σήμα για προσαρμογή· η πρωτεΐνη δίνει την πρώτη ύλη. Αν δεν παίρνεις αρκετή, ανακάμπτεις πιο αργά και χτίζεις λιγότερο μυ από την ίδια δουλειά. Τα γεύματα με αρκετή πρωτεΐνη επίσης σε χορταίνουν για περισσότερη ώρα, κάτι που βοηθά να ελέγχεις πιο εύκολα την όρεξή σου.</p>
                <h2>Τα νούμερα: πόση την ημέρα</h2>
                <p>Η γενική σύσταση για ενήλικες είναι περίπου 0,8 γραμμάρια πρωτεΐνης ανά κιλό σωματικού βάρους την ημέρα. Αυτή η τιμή έχει οριστεί για να προλαμβάνει την έλλειψη στον γενικό πληθυσμό, που στην πλειονότητά του δεν κάνει οργανωμένη άσκηση. Για ενήλικες που προπονούνται τακτικά, οι φορείς αθλητικής διατροφής προτείνουν γενικά περισσότερη: περίπου <strong>1,2 έως 2,0 g ανά κιλό σωματικού βάρους την ημέρα</strong>.</p>
                <ul>
                <li><strong>Περίπου 1,2–1,6 g/kg</strong> ταιριάζουν στους περισσότερους που κάνουν τακτικά μικτή προπόνηση για υγεία και φυσική κατάσταση.</li>
                <li><strong>Περίπου 1,6–2,0 g/kg</strong> είναι ένας λογικός στόχος αν κάνεις σοβαρή προπόνηση με βάρη για να χτίσεις μυ ή αν θέλεις να κρατήσεις τη μυϊκή σου μάζα ενώ τρως με θερμιδικό έλλειμμα.</li>
                </ul>
                <p>Για παράδειγμα, ένα άτομο 70 κιλών που προπονείται τρεις με τέσσερις φορές την εβδομάδα θα στόχευε σε περίπου 85 έως 140 g πρωτεΐνης την ημέρα, ανάλογα με τον στόχο του. Πολύ υψηλότερες ποσότητες είναι απίθανο να προσφέρουν επιπλέον όφελος για τους μύες στους περισσότερους ανθρώπους. Οι μεγαλύτεροι σε ηλικία συχνά ωφελούνται από το πάνω μέρος του εύρους, γιατί με τα χρόνια οι μύες ανταποκρίνονται λιγότερο στην πρωτεΐνη.</p>
                <h2>Μοίρασέ τη μέσα στη μέρα</h2>
                <p>Το σώμα σου αξιοποιεί καλύτερα την πρωτεΐνη όταν έρχεται σε τακτικές μερίδες και όχι σε ένα τεράστιο γεύμα. Μια πρακτική λύση είναι να έχεις μια πηγή πρωτεΐνης σε καθένα από τρία ή τέσσερα γεύματα, με στόχο περίπου 20 έως 40 g κάθε φορά, δηλαδή γύρω στα 0,3 g ανά κιλό σωματικού βάρους ανά γεύμα.</p>
                <h2>Μετράει η ώρα γύρω από την προπόνηση;</h2>
                <p>Ένα γεύμα πλούσιο σε πρωτεΐνη μέσα σε λίγες ώρες πριν ή μετά την προπόνηση είναι καλή συνήθεια, όμως το λεγόμενο «αναβολικό παράθυρο» είναι πολύ μεγαλύτερο απ' ό,τι υποστήριζε ο παλιός κανόνας των 30 λεπτών. Η συνολική ημερήσια πρόσληψη μετράει πολύ περισσότερο από το ακριβές λεπτό που θα φας. Η βασική εξαίρεση είναι η προπόνηση με άδειο στομάχι νωρίς το πρωί: τότε, φάε ένα γεύμα με πρωτεΐνη σύντομα μετά.</p>
                <h2>Πρώτα το φαγητό</h2>
                <p>Μπορείς να καλύψεις τις ανάγκες σου με απλά, καθημερινά γεύματα. Ενδεικτική πρωτεΐνη σε μερικά συνηθισμένα τρόφιμα:</p>
                <ul>
                <li>100 g μαγειρεμένο στήθος κοτόπουλου: περίπου 30 g</li>
                <li>100 g τόνος κονσέρβας, στραγγισμένος: περίπου 25 g</li>
                <li>200 g στραγγιστό γιαούρτι (2% λιπαρά): περίπου 20 g</li>
                <li>2 μεγάλα αυγά: περίπου 12 g</li>
                <li>150 g μαγειρεμένες φακές ή ρεβίθια: περίπου 13 g</li>
                <li>100 g σφιχτό τόφου: περίπου 12–15 g</li>
                <li>30 g σκληρό τυρί, όπως γραβιέρα ή τσένταρ: περίπου 7 g</li>
                </ul>
                <p>Αν ακολουθείς φυτική διατροφή, μπορείς να φτάσεις τους ίδιους στόχους συνδυάζοντας μέσα στη μέρα όσπρια, τόφου, tempeh, ρόφημα σόγιας, ξηρούς καρπούς, σπόρους και δημητριακά ολικής άλεσης. Επειδή ορισμένες φυτικές πρωτεΐνες έχουν λιγότερα από κάποια αμινοξέα, στόχευσε στο πάνω μισό του εύρους και συνδύαζε πηγές.</p>
                <h2>Και η πρωτεΐνη σε σκόνη;</h2>
                <p>Ένα shake είναι απλώς βολικό φαγητό, όχι υποχρέωση. Μπορεί να βοηθήσει τις πιεσμένες μέρες ή όταν δεν έχεις πολλή όρεξη, αλλά δεν δουλεύει καλύτερα από ένα γεύμα με την ίδια ποσότητα πρωτεΐνης. Αν χρησιμοποιείς, διάλεξε ένα προϊόν που έχει ελεγχθεί από ανεξάρτητο φορέα και αντιμετώπισέ το ως συμπλήρωμα και όχι ως τη βάση της διατροφής σου.</p>
                <h2>Ένας απλός τρόπος να ξεκινήσεις</h2>
                <ol>
                <li>Πολλαπλασίασε το βάρος σου σε κιλά επί 1,4 για να βρεις έναν αρχικό ημερήσιο στόχο.</li>
                <li>Διαίρεσε αυτόν τον αριθμό με τα γεύματα που τρως, για να βρεις τον στόχο ανά γεύμα.</li>
                <li>Χτίσε κάθε γεύμα γύρω από τουλάχιστον μία μερίδα πρωτεΐνης στο μέγεθος της παλάμης σου και πρόσθεσε λαχανικά, υδατάνθρακες και λίγα υγιεινά λιπαρά.</li>
                <li>Κατάγραψε τι τρως για τρεις με τέσσερις μέρες, για να δεις αν είσαι κοντά στον στόχο, και μετά προσάρμοσε.</li>
                </ol>
                <h2>Συμπέρασμα</h2>
                <p>Αν προπονείσαι τακτικά, στόχευσε σε περίπου 1,2 έως 2,0 g πρωτεΐνης ανά κιλό σωματικού βάρους την ημέρα, μοιρασμένα στα γεύματά σου και κυρίως από πραγματικό φαγητό. Πέτυχέ το με συνέπεια, συνέχισε να προπονείσαι προοδευτικά και κοιμήσου καλά, και το σώμα σου θα έχει ό,τι χρειάζεται για να προσαρμοστεί.</p>
                <p><strong>Σημείωση:</strong> Το άρθρο αυτό παρέχει γενικές πληροφορίες και δεν αποτελεί ιατρική συμβουλή — αν έχεις νεφρική νόσο ή άλλη πάθηση, ή αν είσαι έγκυος, συζήτησέ το με γιατρό ή διαιτολόγο πριν αλλάξεις τη διατροφή σου.</p>
                """,
            CoverImageUrl: "/images/blog/how-much-protein-do-you-need.webp",
            AuthorName: "FitEdu",
            Tags: ["Nutrition", "Recovery"],
            IsPublished: true
        ),

        new SaveBlogArticleRequest(
            TitleEn: "Sleep Is Your Best Recovery Tool",
            TitleEl: "Ο ύπνος είναι το καλύτερο εργαλείο αποκατάστασης",
            Slug: "sleep-is-your-best-recovery-tool",
            SummaryEn: "No supplement or gadget comes close to a good night's sleep. Learn why sleep drives your training results and pick up practical habits to sleep longer and better.",
            SummaryEl: "Κανένα συμπλήρωμα και κανένα gadget δεν πλησιάζει έναν καλό νυχτερινό ύπνο. Μάθε γιατί ο ύπνος καθορίζει τα αποτελέσματα της προπόνησής σου και υιοθέτησε πρακτικές συνήθειες για να κοιμάσαι περισσότερο και καλύτερα.",
            ContentEn: """
                <p>People spend money on massage guns, compression gear and supplements in the name of recovery, yet the most powerful recovery tool is free and you use it every night. Sleep is when much of the repair work after training takes place. Cut it short, and every other part of your programme works less well.</p>
                <h2>What happens while you sleep</h2>
                <p>During deep sleep, your body releases a large share of its daily growth hormone, which supports tissue repair. Your nervous system recovers, and your brain consolidates the movement skills you practised, which is one reason a new exercise often feels smoother after a good night. Sleep also helps regulate the hormones that control hunger and fullness, so poor sleep can make your appetite harder to manage.</p>
                <h2>How much is enough?</h2>
                <p>Health organisations recommend that adults get <strong>7 to 9 hours</strong> of sleep per night, and teenagers more. Many people who train hard feel and perform best toward the upper end of that range. A simple test: if you need an alarm every single day, sleep in for hours at weekends or feel drowsy every afternoon, you are probably not getting enough.</p>
                <h2>What short sleep does to your training</h2>
                <p>Even a few nights of restricted sleep can make workouts feel harder than they are, reduce endurance, slow your reaction time and sap your motivation. Over weeks, chronic sleep loss in active people is associated with slower recovery, more frequent colds and a higher risk of injury. It also tends to nudge your food choices towards sugary, high-calorie snacks.</p>
                <h2>Build a better sleep routine</h2>
                <ol>
                <li><strong>Keep a consistent schedule.</strong> Go to bed and wake up at roughly the same times every day, weekends included. Your body clock loves routine.</li>
                <li><strong>Get morning daylight.</strong> Spending 10 to 20 minutes outside early in the day helps set your internal clock, so you feel sleepy more easily at night.</li>
                <li><strong>Make the room dark, quiet and cool.</strong> Many people sleep best with the bedroom at around 16 to 19 °C. Blackout curtains or an eye mask help.</li>
                <li><strong>Watch your caffeine timing.</strong> Caffeine can stay in your system for many hours. Try to have your last coffee, tea, energy drink or pre-workout at least 6–8 hours before bed.</li>
                <li><strong>Go easy on alcohol.</strong> A drink may make you drowsy, but alcohol fragments sleep in the second half of the night and lowers its quality.</li>
                <li><strong>Create a wind-down buffer.</strong> Spend the last 30 to 60 minutes before bed away from emails, news feeds and bright screens. Read, stretch gently or try slow breathing.</li>
                <li><strong>Time hard workouts sensibly.</strong> Exercise generally improves sleep. If a late, intense session leaves you wired, finish it at least two hours before bed or move it earlier.</li>
                </ol>
                <h2>Naps: a useful top-up</h2>
                <p>If your night was short, a nap of 20 to 30 minutes in the early afternoon can restore alertness without that heavy, groggy feeling afterwards. Longer or late-afternoon naps may make it harder to fall asleep at night, so keep them short and early.</p>
                <h2>Recovery days count too</h2>
                <p>Sleep works best alongside sensible training. Plan at least one or two easier days each week, keep hard sessions for the same muscle groups at least 48 hours apart, and use light activity such as walking or easy mobility work on rest days. Good sleep turns those easier days into real progress.</p>
                <h2>When to get help</h2>
                <p>Good habits fix many sleep problems, but not all of them. If you snore loudly, wake up gasping, feel exhausted despite enough time in bed, or struggle with insomnia for weeks, talk to a doctor. Conditions such as sleep apnoea are common, treatable and worth ruling out.</p>
                <h2>The bottom line</h2>
                <p>Training is the stimulus, but you actually improve during recovery, and sleep is the biggest part of recovery. Treat your 7 to 9 hours as part of your programme, protect them the way you protect your workouts, and you will feel the difference both in and out of the gym.</p>
                <p><strong>Note:</strong> This article is general information, not medical advice — consult a health professional if you have ongoing sleep problems or a health condition.</p>
                """,
            ContentEl: """
                <p>Ξοδεύουμε χρήματα σε πιστόλια μασάζ, ρούχα συμπίεσης και συμπληρώματα στο όνομα της αποκατάστασης, όμως το πιο ισχυρό εργαλείο αποκατάστασης είναι δωρεάν και το χρησιμοποιείς κάθε βράδυ. Στον ύπνο γίνεται μεγάλο μέρος της «επισκευής» μετά την προπόνηση. Αν τον περικόπτεις, όλα τα υπόλοιπα κομμάτια του προγράμματός σου αποδίδουν λιγότερο.</p>
                <h2>Τι συμβαίνει όσο κοιμάσαι</h2>
                <p>Στον βαθύ ύπνο το σώμα σου εκκρίνει μεγάλο μέρος της ημερήσιας αυξητικής ορμόνης, που στηρίζει την επιδιόρθωση των ιστών. Το νευρικό σου σύστημα ανακάμπτει και ο εγκέφαλος εδραιώνει τις κινητικές δεξιότητες που εξασκήθηκες, γι' αυτό μια καινούργια άσκηση συχνά βγαίνει πιο «στρωτά» μετά από έναν καλό ύπνο. Ο ύπνος βοηθά επίσης στη ρύθμιση των ορμονών που ελέγχουν την πείνα και τον κορεσμό, οπότε ο κακός ύπνος μπορεί να δυσκολέψει τον έλεγχο της όρεξης.</p>
                <h2>Πόσος ύπνος αρκεί;</h2>
                <p>Οι οργανισμοί υγείας συστήνουν στους ενήλικες <strong>7 έως 9 ώρες</strong> ύπνου κάθε βράδυ, και στους εφήβους περισσότερες. Πολλοί που προπονούνται έντονα νιώθουν και αποδίδουν καλύτερα προς το πάνω όριο. Ένα απλό τεστ: αν χρειάζεσαι ξυπνητήρι κάθε μέρα, κοιμάσαι ατελείωτες ώρες τα Σαββατοκύριακα ή νυστάζεις κάθε απόγευμα, μάλλον δεν κοιμάσαι αρκετά.</p>
                <h2>Τι κάνει ο λίγος ύπνος στην προπόνησή σου</h2>
                <p>Ακόμη και λίγες νύχτες περιορισμένου ύπνου μπορούν να κάνουν την προπόνηση να μοιάζει πιο δύσκολη απ' ό,τι είναι, να μειώσουν την αντοχή, να επιβραδύνουν τα αντανακλαστικά και να ρίξουν το κίνητρο. Σε βάθος εβδομάδων, η χρόνια έλλειψη ύπνου σε δραστήρια άτομα συνδέεται με πιο αργή αποκατάσταση, πιο συχνά κρυολογήματα και αυξημένο κίνδυνο τραυματισμού. Επίσης σε σπρώχνει συνήθως προς γλυκά σνακ με πολλές θερμίδες.</p>
                <h2>Χτίσε μια καλύτερη ρουτίνα ύπνου</h2>
                <ol>
                <li><strong>Κράτα σταθερό πρόγραμμα.</strong> Πήγαινε για ύπνο και ξύπνα περίπου τις ίδιες ώρες κάθε μέρα, μαζί με τα Σαββατοκύριακα. Το βιολογικό σου ρολόι λατρεύει τη ρουτίνα.</li>
                <li><strong>Πάρε πρωινό φως.</strong> 10 έως 20 λεπτά έξω νωρίς μέσα στη μέρα βοηθούν να ρυθμιστεί το εσωτερικό σου ρολόι, ώστε να νυστάζεις πιο εύκολα το βράδυ.</li>
                <li><strong>Κάνε το δωμάτιο σκοτεινό, ήσυχο και δροσερό.</strong> Πολλοί κοιμούνται καλύτερα με θερμοκρασία δωματίου γύρω στους 16 με 19 °C. Οι κουρτίνες συσκότισης ή μια μάσκα ματιών βοηθούν.</li>
                <li><strong>Πρόσεχε πότε πίνεις καφεΐνη.</strong> Η καφεΐνη μπορεί να μείνει στον οργανισμό σου πολλές ώρες. Προσπάθησε ο τελευταίος καφές, τσάι, ενεργειακό ποτό ή pre-workout να είναι τουλάχιστον 6–8 ώρες πριν τον ύπνο.</li>
                <li><strong>Μην το παρακάνεις με το αλκοόλ.</strong> Ένα ποτό μπορεί να σε νυστάξει, όμως το αλκοόλ «σπάει» τον ύπνο στο δεύτερο μισό της νύχτας και μειώνει την ποιότητά του.</li>
                <li><strong>Φτιάξε ένα διάστημα χαλάρωσης.</strong> Πέρασε τα τελευταία 30 με 60 λεπτά πριν τον ύπνο μακριά από email, ειδήσεις και φωτεινές οθόνες. Διάβασε, κάνε ήπιες διατάσεις ή δοκίμασε αργή αναπνοή.</li>
                <li><strong>Βάλε τις έντονες προπονήσεις στη σωστή ώρα.</strong> Η άσκηση γενικά βελτιώνει τον ύπνο. Αν μια έντονη προπόνηση αργά το βράδυ σε κρατά σε υπερένταση, τελείωσέ τη τουλάχιστον δύο ώρες πριν τον ύπνο ή μετάφερέ τη νωρίτερα.</li>
                </ol>
                <h2>Μεσημεριανός υπνάκος: ένα χρήσιμο συμπλήρωμα</h2>
                <p>Αν η νύχτα σου ήταν σύντομη, ένας υπνάκος 20 με 30 λεπτών νωρίς το απόγευμα μπορεί να σου επαναφέρει την εγρήγορση χωρίς εκείνη τη βαριά, θολή αίσθηση μετά. Μεγαλύτεροι υπνάκοι ή υπνάκοι αργά το απόγευμα μπορεί να σε δυσκολέψουν να κοιμηθείς το βράδυ, οπότε κράτα τους σύντομους και νωρίς.</p>
                <h2>Μετράνε και οι μέρες αποκατάστασης</h2>
                <p>Ο ύπνος αποδίδει καλύτερα μαζί με λογική προπόνηση. Βάλε στο πρόγραμμα τουλάχιστον μία ή δύο πιο εύκολες μέρες την εβδομάδα, άφηνε τουλάχιστον 48 ώρες ανάμεσα σε έντονες προπονήσεις για τις ίδιες μυϊκές ομάδες και κάνε ελαφριά δραστηριότητα, όπως περπάτημα ή εύκολη κινητικότητα, τις μέρες ξεκούρασης. Ο καλός ύπνος μετατρέπει αυτές τις πιο εύκολες μέρες σε πραγματική πρόοδο.</p>
                <h2>Πότε να ζητήσεις βοήθεια</h2>
                <p>Οι καλές συνήθειες λύνουν πολλά προβλήματα ύπνου, αλλά όχι όλα. Αν ροχαλίζεις δυνατά, ξυπνάς με αίσθημα πνιγμού, νιώθεις εξάντληση παρότι περνάς αρκετές ώρες στο κρεβάτι ή παλεύεις με αϋπνία για εβδομάδες, μίλησε με γιατρό. Παθήσεις όπως η άπνοια ύπνου είναι συχνές, αντιμετωπίζονται και αξίζει να αποκλειστούν.</p>
                <h2>Συμπέρασμα</h2>
                <p>Η προπόνηση είναι το ερέθισμα, αλλά η βελτίωση συμβαίνει στην αποκατάσταση, και ο ύπνος είναι το μεγαλύτερο κομμάτι της. Αντιμετώπισε τις 7 έως 9 ώρες σου ως μέρος του προγράμματος, προστάτεψέ τες όπως προστατεύεις τις προπονήσεις σου, και θα δεις τη διαφορά μέσα και έξω από το γυμναστήριο.</p>
                <p><strong>Σημείωση:</strong> Το άρθρο αυτό παρέχει γενικές πληροφορίες και δεν αποτελεί ιατρική συμβουλή — συμβουλέψου επαγγελματία υγείας αν έχεις συνεχή προβλήματα ύπνου ή κάποια πάθηση.</p>
                """,
            CoverImageUrl: "/images/blog/sleep-is-your-best-recovery-tool.webp",
            AuthorName: "FitEdu",
            Tags: ["Recovery"],
            IsPublished: true
        ),

        new SaveBlogArticleRequest(
            TitleEn: "A Five-Minute Warm-Up Routine You Can Do Anywhere",
            TitleEl: "Ζέσταμα πέντε λεπτών που μπορείς να κάνεις παντού",
            Slug: "five-minute-warm-up-routine",
            SummaryEn: "Skipping the warm-up is tempting when time is short. This five-minute, no-equipment routine raises your temperature, wakes up your joints and primes the muscles you are about to train.",
            SummaryEl: "Όταν ο χρόνος πιέζει, ο πειρασμός να παραλείψεις το ζέσταμα είναι μεγάλος. Αυτή η ρουτίνα πέντε λεπτών χωρίς εξοπλισμό ανεβάζει τη θερμοκρασία σου, ξυπνά τις αρθρώσεις και προετοιμάζει τους μύες που θα προπονήσεις.",
            ContentEn: """
                <p>When you are short on time, the warm-up is usually the first thing to go. That is a shame, because five well-spent minutes can make the rest of your session feel better, help you perform better and prepare your joints for the work ahead. You do not need a long routine or any equipment. You need a plan.</p>
                <h2>What a warm-up should do</h2>
                <p>A good warm-up has three tasks. First, it raises your body temperature and heart rate gradually, so your muscles contract more efficiently and you breathe more comfortably once the hard work starts. Second, it takes your joints through the ranges of motion you are about to use. Third, it rehearses the movement patterns of the session at low intensity, so your first real set is not also your first attempt of the day.</p>
                <p>Research suggests that long static stretches held right before training can briefly reduce power output, especially when each one is held for a minute or more. For that reason, most coaches prefer dynamic movements before a workout and save longer holds for afterwards or for separate mobility sessions.</p>
                <h2>The five-minute routine</h2>
                <p>Do each move for the time listed, flowing from one to the next with little or no rest. Move smoothly and stay well within a range you control; the goal is to feel warm and loose, not tired.</p>
                <ol>
                <li><strong>March or jog on the spot — 45 seconds.</strong> Start with a brisk march, then pick up the pace to a light jog if your joints feel ready. Let your arms swing naturally.</li>
                <li><strong>Arm circles — 30 seconds.</strong> Small circles forward that grow gradually larger, then reverse direction. Keep your ribs down and your neck relaxed.</li>
                <li><strong>Hip circles and leg swings — 45 seconds.</strong> Circle your hips in both directions, then hold a wall and swing one leg forward and back, slowly increasing the range. Switch legs halfway.</li>
                <li><strong>Cat-cow — 30 seconds.</strong> On your hands and knees, slowly round your back towards the ceiling, then let it sag gently as you lift your chest. Move with your breath.</li>
                <li><strong>World's greatest stretch — 60 seconds.</strong> Step into a long lunge, place both hands on the floor inside your front foot, then rotate your chest and reach the arm on the same side as your front leg towards the ceiling. Return, step back and switch sides. Alternate for the full minute.</li>
                <li><strong>Bodyweight squats — 45 seconds.</strong> Slow and controlled, sitting down between your heels with your chest tall. Go only as deep as you can while keeping your heels on the floor.</li>
                <li><strong>Inchworm to plank — 45 seconds.</strong> From standing, hinge forward, walk your hands out to a plank, pause, then walk them back and stand up. Bend your knees as much as you need to.</li>
                </ol>
                <h2>Make it specific to your session</h2>
                <p>After the general routine, add one or two lighter "ramp-up" sets of your first main exercise. If you are squatting with a barbell, do a set with just the bar and another at about half your working weight. If today is a run, finish with a few 20 to 30 second strides at a gradually faster pace. If you are doing a HIIT session, perform the first interval at an easy pace. These specific sets take only a minute or two and are the most effective part of any warm-up.</p>
                <h2>Adjusting the routine</h2>
                <ul>
                <li><strong>Cold morning or very early session:</strong> do an extra minute of marching or jogging before the rest of the routine.</li>
                <li><strong>Upper-body day:</strong> add 30 seconds of wall slides, or band pull-aparts if you have a resistance band.</li>
                <li><strong>Stiff hips from sitting:</strong> spend a little longer on the world's greatest stretch and the leg swings.</li>
                <li><strong>Limited space or thin floors:</strong> replace the jog with step-jacks, stepping one foot out to the side as you raise your arms, to keep things quiet.</li>
                </ul>
                <h2>Safety</h2>
                <ul>
                <li>Warm-up movements should never be painful. If one hurts, skip it or make the range smaller.</li>
                <li>If you feel dizzy, short of breath or have any chest discomfort, stop and seek medical advice before training.</li>
                <li>Your warm-up is preparation, not a workout. Finish it feeling ready, not drained.</li>
                </ul>
                <h2>The bottom line</h2>
                <p>Five minutes is enough to raise your temperature, mobilise your joints and rehearse the session ahead. Make this routine a fixed part of every workout and your training will feel smoother from the very first set.</p>
                <p><strong>Note:</strong> This article is general information, not medical advice — consult a professional before starting if you have a health condition.</p>
                """,
            ContentEl: """
                <p>Όταν ο χρόνος πιέζει, το ζέσταμα είναι συνήθως το πρώτο που κόβεται. Κρίμα, γιατί πέντε καλά αξιοποιημένα λεπτά μπορούν να κάνουν την υπόλοιπη προπόνηση πιο ευχάριστη, να σε βοηθήσουν να αποδώσεις καλύτερα και να προετοιμάσουν τις αρθρώσεις σου για όσα ακολουθούν. Δεν χρειάζεσαι μεγάλη ρουτίνα ούτε εξοπλισμό. Χρειάζεσαι ένα σχέδιο.</p>
                <h2>Τι πρέπει να κάνει ένα ζέσταμα</h2>
                <p>Ένα καλό ζέσταμα έχει τρεις στόχους. Πρώτον, ανεβάζει σταδιακά τη θερμοκρασία του σώματος και τους παλμούς, ώστε οι μύες να συσπώνται πιο αποτελεσματικά και να αναπνέεις πιο άνετα όταν ξεκινήσει το δύσκολο κομμάτι. Δεύτερον, περνά τις αρθρώσεις σου από τα εύρη κίνησης που θα χρησιμοποιήσεις. Τρίτον, κάνει «πρόβα» στα μοτίβα κίνησης της προπόνησης σε χαμηλή ένταση, ώστε το πρώτο σου πραγματικό σετ να μην είναι και η πρώτη σου προσπάθεια της ημέρας.</p>
                <p>Τα ερευνητικά δεδομένα δείχνουν ότι οι παρατεταμένες στατικές διατάσεις ακριβώς πριν την προπόνηση μπορεί να μειώσουν προσωρινά την ισχύ, ειδικά όταν η καθεμία κρατιέται για ένα λεπτό ή περισσότερο. Γι' αυτό οι περισσότεροι προπονητές προτιμούν δυναμικές κινήσεις πριν την προπόνηση και κρατούν τις μεγάλες διατάσεις για μετά ή για ξεχωριστές προπονήσεις κινητικότητας.</p>
                <h2>Η ρουτίνα των πέντε λεπτών</h2>
                <p>Κάνε κάθε κίνηση για τον χρόνο που αναφέρεται, περνώντας από τη μία στην άλλη με ελάχιστη ή καθόλου ξεκούραση. Κινήσου ομαλά και μείνε άνετα μέσα στο εύρος που ελέγχεις· ο στόχος είναι να νιώσεις ζεστασιά και ευκινησία, όχι κούραση.</p>
                <ol>
                <li><strong>Βάδισμα ή τροχάδην επί τόπου — 45 δευτερόλεπτα.</strong> Ξεκίνα με γρήγορο βάδισμα επί τόπου και ανέβασε τον ρυθμό σε ελαφρύ τροχάδην αν οι αρθρώσεις σου είναι έτοιμες. Άφησε τα χέρια να κινούνται φυσικά.</li>
                <li><strong>Κύκλοι χεριών — 30 δευτερόλεπτα.</strong> Μικροί κύκλοι προς τα εμπρός που μεγαλώνουν σταδιακά, και μετά αλλαγή φοράς. Κράτα τα πλευρά κάτω και τον αυχένα χαλαρό.</li>
                <li><strong>Κύκλοι λεκάνης και αιωρήσεις ποδιών — 45 δευτερόλεπτα.</strong> Κάνε κύκλους με τη λεκάνη και προς τις δύο κατευθύνσεις, μετά στηρίξου σε έναν τοίχο και αιώρησε το ένα πόδι μπρος-πίσω, μεγαλώνοντας σιγά σιγά το εύρος. Άλλαξε πόδι στη μέση.</li>
                <li><strong>Cat-cow (γάτα-αγελάδα) — 30 δευτερόλεπτα.</strong> Στα τέσσερα, καμπύλωσε αργά την πλάτη προς το ταβάνι και μετά άφησέ την να βουλιάξει απαλά καθώς σηκώνεις το στήθος. Συγχρόνισε την κίνηση με την αναπνοή σου.</li>
                <li><strong>World's greatest stretch (διάταση με διασκελισμό και στροφή) — 60 δευτερόλεπτα.</strong> Κάνε έναν μεγάλο διασκελισμό (lunge), ακούμπησε και τις δύο παλάμες στο πάτωμα στην εσωτερική πλευρά του μπροστινού ποδιού, μετά στρίψε τον κορμό και τεντώσου προς το ταβάνι με το χέρι της ίδιας πλευράς με το μπροστινό πόδι. Γύρνα πίσω, μάζεψε το πόδι και άλλαξε πλευρά. Εναλλάξ για ένα ολόκληρο λεπτό.</li>
                <li><strong>Squats με βάρος σώματος — 45 δευτερόλεπτα.</strong> Αργά και ελεγχόμενα, «κάθισε» ανάμεσα στις φτέρνες σου με το στήθος ψηλά. Κατέβα μόνο όσο βαθιά μπορείς κρατώντας τις φτέρνες στο πάτωμα.</li>
                <li><strong>Inchworm σε plank — 45 δευτερόλεπτα.</strong> Από όρθια θέση, σκύψε λυγίζοντας από τα ισχία, περπάτα με τα χέρια μπροστά μέχρι τη θέση plank, κάνε μια μικρή παύση, και μετά περπάτα με τα χέρια πίσω και σήκω. Λύγισε τα γόνατα όσο χρειάζεται.</li>
                </ol>
                <h2>Κάν' το ειδικό για την προπόνησή σου</h2>
                <p>Μετά τη γενική ρουτίνα, πρόσθεσε ένα ή δύο ελαφρύτερα σετ «ανεβάσματος» στην πρώτη βασική άσκηση. Αν κάνεις squat με μπάρα, κάνε ένα σετ μόνο με την μπάρα και ένα με περίπου το μισό βάρος εργασίας. Αν σήμερα τρέχεις, τελείωσε με μερικές επιταχύνσεις (strides) των 20 έως 30 δευτερολέπτων σε σταδιακά πιο γρήγορο ρυθμό. Αν κάνεις HIIT, κάνε το πρώτο διάστημα σε χαλαρό ρυθμό. Αυτά τα ειδικά σετ παίρνουν μόνο ένα με δύο λεπτά και είναι το πιο αποτελεσματικό κομμάτι κάθε ζεστάματος.</p>
                <h2>Προσαρμογές της ρουτίνας</h2>
                <ul>
                <li><strong>Κρύο πρωινό ή πολύ πρωινή προπόνηση:</strong> κάνε ένα επιπλέον λεπτό βάδισμα ή τροχάδην πριν από την υπόλοιπη ρουτίνα.</li>
                <li><strong>Μέρα άνω κορμού:</strong> πρόσθεσε 30 δευτερόλεπτα wall slides (ολισθήσεις χεριών στον τοίχο) ή band pull-aparts, αν έχεις λάστιχο αντίστασης.</li>
                <li><strong>Σφιγμένα ισχία από το πολύ καθισιό:</strong> αφιέρωσε λίγο περισσότερο χρόνο στο world's greatest stretch και στις αιωρήσεις ποδιών.</li>
                <li><strong>Λίγος χώρος ή λεπτό πάτωμα:</strong> αντικατάστησε το τροχάδην με step-jacks, βγάζοντας το ένα πόδι στο πλάι καθώς σηκώνεις τα χέρια, για να μην κάνεις θόρυβο.</li>
                </ul>
                <h2>Ασφάλεια</h2>
                <ul>
                <li>Οι κινήσεις του ζεστάματος δεν πρέπει ποτέ να πονάνε. Αν κάποια πονάει, παράλειψέ τη ή μίκρυνε το εύρος.</li>
                <li>Αν νιώσεις ζάλη, δύσπνοια ή οποιαδήποτε ενόχληση στο στήθος, σταμάτα και ζήτησε ιατρική συμβουλή πριν προπονηθείς.</li>
                <li>Το ζέσταμα είναι προετοιμασία, όχι προπόνηση. Στο τέλος του πρέπει να νιώθεις ετοιμότητα, όχι εξάντληση.</li>
                </ul>
                <h2>Συμπέρασμα</h2>
                <p>Πέντε λεπτά αρκούν για να ανεβάσεις τη θερμοκρασία σου, να κινητοποιήσεις τις αρθρώσεις και να κάνεις πρόβα την προπόνηση που ακολουθεί. Κάνε αυτή τη ρουτίνα σταθερό κομμάτι κάθε προπόνησης και η προπόνησή σου θα κυλά πιο «στρωτά» από το πρώτο κιόλας σετ.</p>
                <p><strong>Σημείωση:</strong> Το άρθρο αυτό παρέχει γενικές πληροφορίες και δεν αποτελεί ιατρική συμβουλή — συμβουλέψου επαγγελματία πριν ξεκινήσεις αν έχεις κάποια πάθηση.</p>
                """,
            CoverImageUrl: "/images/blog/five-minute-warm-up-routine.webp",
            AuthorName: "FitEdu",
            Tags: ["Mobility", "Beginners"],
            IsPublished: true
        )
    ];
}
