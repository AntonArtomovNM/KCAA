import getData from "./dal.js"
const charPath = "CharacterSettings.json";
const quarPath = "QuarterSettings.json";

(async () => {
	let data = await getData(charPath);
	const characters = document.getElementById('chars');
	for (var i = 0; i < data.length; i++) {
		let charTitle = document.createElement(`tr`);
		let heading = document.createElement('th');
		heading.textContent = data[i]['DisplayName'];
		heading.setAttribute("colspan", "2");
		charTitle.append(heading);
		let charContent = document.createElement(`tr`);
		let img = document.createElement('img');
        img.src = `${data[i]['PhotoUri']}`;
		let text = document.createElement('p');
        text.textContent = data[i]['Description'].replace(" (Currently: {0})", "");
		let image = document.createElement('td');
		image.append(img);
		let disc = document.createElement('td');
		disc.append(text);
		if(i%2==0){
			charContent.append(image, disc);
		}	
		else{
			charContent.append(disc, image);
		}
		characters.append(charTitle, charContent); 
	}

	let spdata = await getData(quarPath);
	const gallery = document.getElementById('slideshow-container');
	for (var i = 0; i < spdata.length; i++) {
		let divPhoto = document.createElement('div');
		divPhoto.setAttribute('class', 'mySlides fade')
		if(spdata[i]['Name'].toString().includes('sp-')){
			let quarterImg = document.createElement('img');
        	quarterImg.src = `${spdata[i]['PhotoUri']}`;
			let quarterTitle = document.createElement(`div`);
			quarterTitle.setAttribute("class", "text");
			quarterTitle.textContent = spdata[i]['DisplayName'];
			divPhoto.append(quarterImg, quarterTitle);
			gallery.append(divPhoto);
		}
	}

	let prev = document.createElement('div');
	prev.setAttribute('class', 'prev galBtn');
	
	let next = document.createElement('div');
	next.setAttribute('class', 'next galBtn');
	
	gallery.append(prev, next);

	let slideIndex = 1;
	showSlides(slideIndex);

	function plusSlides(n) {
		showSlides(slideIndex += n);
	}

	prev.addEventListener("click", plusSlides(-1));
	next.addEventListener("click", plusSlides(1));

	function showSlides(n) {
		let i;
		let slides = document.getElementsByClassName("mySlides");
		if (n > slides.length) {slideIndex = 1}
		if (n < 1) {slideIndex = slides.length}
		for (i = 0; i < slides.length; i++) {
			slides[i].style.display = "none";
		}
		slides[slideIndex-1].style.display = "block";
		}
})();

let upbutton = document.getElementById("up");

(window).scroll(function() {
	if ($(window).scrollTop() > 300) {
		console.log("up");
		upbutton.setAttribute('visibility', 'visible');
	} 
	else {
		console.log("down");
		upbutton.setAttribute('visibility', 'hidden');
	}
  });
